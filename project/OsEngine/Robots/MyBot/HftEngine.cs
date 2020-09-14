using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using System.IO;
using System.Windows;
using System.Threading;
using OsEngine.Market;
using OsEngine.Market.Servers;


namespace OsEngine.Robots.MyBot
{
    public class HftEngine : BotPanel
    {
        #region Параметры робота
        public List<IServer> Servers = new List<IServer>();         // список серверов робота
        public List<Portfolio> Portfolios;                          // список портфелей робота
        public List<Security> Securities;                           // список инструментов робота
        private List<Order> _orders = new List<Order>();            // список ордеров робота
        private List<MyPosition> _positions = new List<MyPosition>();// список позиций робота
        private DateTime _lastCheckTime;                            // время последней проверки ордеров на время жизни

        // Настроечные параметры робота
        public bool IsOnPositionSupport = false;                // режим сопровождения позиций робота - включен/выключен
        public int OrderLifeTime = 20;                          // время жизни ордера в секундах
        public int Profit = 20;                                 // тейкпрофит в шагах цены
        public int Stop = 20;                                   // стоплос в шагах цены
        #endregion

        /// <summary>
        /// Конструктор класса робота
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Программа, запустившая робота</param>
        public HftEngine(string name, StartProgram startProgram) : base(name, startProgram)
        {
            // подписка на событие создание сервера
            ServerMaster.ServerCreateEvent += ServerMaster_ServerCreateEvent;

            // подписка на сервисные события
            DeleteEvent += HftEngine_DeleteEvent;

            // загрузка настроечных параметров
            Load();
        }

        #region Сервисная логика
        public override string GetNameStrategyType()
        {
            return "HftEngine";
        }

        public override void ShowIndividualSettingsDialog()
        {
            HftEngineUi ui = new HftEngineUi(this);
            ui.Show();
        }

        /// <summary>
        /// Сохранение настроек робота
        /// </summary>
        public void Save()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt", false))
                {
                    writer.WriteLine(IsOnPositionSupport);
                    writer.WriteLine(OrderLifeTime);
                    writer.WriteLine(Profit);
                    writer.WriteLine(Stop);

                    writer.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не могу сохранить настройки робота");
            }
        }

        /// <summary>
        /// Загрузка настроек робота
        /// </summary>
        private void Load()
        {
            if (!File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
                {
                    IsOnPositionSupport = Convert.ToBoolean(reader.ReadLine());
                    OrderLifeTime = Convert.ToInt32(reader.ReadLine());
                    Profit = Convert.ToInt32(reader.ReadLine());
                    Stop = Convert.ToInt32(reader.ReadLine());

                    reader.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не могу загрузить настройки робота");
            }
        }

        /// <summary>
        /// Обработчик события удаления пользователем робота
        /// </summary>
        private void HftEngine_DeleteEvent()
        {
            if (File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt");
            }
        }

        /// <summary>
        /// Обработчик события создания нового сервера
        /// </summary>
        /// <param name="newServer">Новый сервер</param>
        private void ServerMaster_ServerCreateEvent(IServer newServer)
        {
            Servers.Add(newServer);
            newServer.PortfoliosChangeEvent += _server_PortfoliosChangeEvent;
            newServer.SecuritiesChangeEvent += _server_SecuritiesChangeEvent;
            newServer.NewOrderIncomeEvent += NewServer_NewOrderIncomeEvent;         // событие изменения ордера
            newServer.NewMyTradeEvent += NewServer_NewMyTradeEvent;                 // событие появления трейда по позициям моего портфеля
            newServer.NewTradeEvent += NewServer_NewTradeEvent;                     // событие появлениия трейда в таблице обезличенных сделок
        }

        /// <summary>
        /// Обработчик события изменения портфеля у сервера
        /// </summary>
        /// <param name="portfolios">Портфель</param>
        private void _server_PortfoliosChangeEvent(List<Portfolio> portfolios)
        {
            Portfolios = portfolios;
        }

        /// <summary>
        /// Обработчик события изменения инструментов у сервера
        /// </summary>
        /// <param name="securities"></param>
        private void _server_SecuritiesChangeEvent(List<Security> securities)
        {
            Securities = securities;
        }

       

        /// <summary>
        /// Смена сервера, выбранного пользователем
        /// </summary>
        /// <param name="serverType"></param>
        public void ChangeServer(ServerType serverType)
        {
            IServer newServer = null;
            List<IServer> allServers = ServerMaster.GetServers();

            for (int i = 0; i < allServers.Count; i++)
            {
                if (serverType == allServers[i].ServerType)
                {
                    newServer = allServers[i];
                    break;
                }
            }

            if (newServer == null)
            {
                return;
            }

            Portfolios = newServer.Portfolios;
            Securities = newServer.Securities;

        }

        #endregion

        #region Торговая логика
        /// <summary>
        /// Выставление ордера на биржу
        /// </summary>
        /// <param name="server">Сервер</param>
        /// <param name="portfolio">Портфель</param>
        /// <param name="security">Инструмент</param>
        /// <param name="price">Цена</param>
        /// <param name="volume">Объем</param>
        /// <param name="orderSide">Направление Buy/Sell</param>
        public void SendOrder(ServerType server, string portfolio, string security, decimal price, decimal volume, Side orderSide)
        {
            // ищем переданный полученный server в списке серверов робота
            IServer myServer = null;
            for (int i = 0; i < Servers.Count; i++)
            {
                if(Servers[i].ServerType == server)
                {
                    myServer = Servers[i];
                    break;
                }
            }

            // если не нашли сервер, то выходим из метода,
            // иначе составляет ордер
            if(myServer == null)
            {
                return;
            }

            // составляем ордер и добавляем ордер в список ордеров бота
            Order order = new Order();
            order.PortfolioNumber = portfolio;
            order.SecurityNameCode = security;
            order.Price = price;
            order.Volume = volume;
            order.Side = orderSide;
            order.ServerType = server;
            order.NumberUser = NumberGen.GetNumberOrder(this.StartProgram);

            _orders.Add(order);

            // отправляем ордер на исполнение
            myServer.ExecuteOrder(order);
        }

        /// <summary>
        /// Отзыв всех ордеров с биржи
        /// </summary>
        public void RejectAllOrders()
        {
            // обходим все ордера робота
            for (int i = 0; i < _orders.Count; i++)
            {
                // если ордер активен и имеет идентификатор биржи,
                // то проверяем, относится ли ордер к нашему серверу,
                // если относится, то снимаем ордер
                if(_orders[i].State == OrderStateType.Activ &&
                    string.IsNullOrEmpty(_orders[i].NumberMarket) == false)
                {
                    RejectOrder(_orders[i]);
                }
            }
        }

        /// <summary>
        /// Отзыв ордера с биржи
        /// </summary>
        /// <param name="order">Ордер</param>
        public void RejectOrder(Order order)
        {
            // ищем переданный server в списке серверов робота
            IServer myServer = null;
            for (int j = 0; j < Servers.Count; j++)
            {
                if (Servers[j].ServerType == order.ServerType)
                {
                    myServer = Servers[j];
                    break;
                }
            }

            // если не нашли, то переходим к проверке следующего ордера
            if (myServer == null)
            {
                return;
            }

            // снимаем ордер
            myServer.CancelOrder(order);
        }

        /// <summary>
        /// Обработчик события изменения ордера
        /// </summary>
        /// <param name="newOrder">Ордер</param>
        private void NewServer_NewOrderIncomeEvent(Order newOrder)
        {
            // в списке ордеров робота ищем ордер, который пришел в обработчик
            for (int i = 0; i < _orders.Count; i++)
            {
                // если нашли, то сохраняем идендификатор ордера, присвоенного биржей,
                // время отклика биржи на ордер и статус ордера
                if (_orders[i].NumberUser == newOrder.NumberUser)
                {
                    if (string.IsNullOrEmpty(_orders[i].NumberMarket))
                    {
                        _orders[i].NumberMarket = newOrder.NumberMarket;
                        _orders[i].TimeCallBack = GetServer(newOrder.ServerType).ServerTime;
                    }

                    _orders[i].State = newOrder.State;
                }
            }
        }

        /// <summary>
        /// Получение сервера
        /// </summary>
        /// <param name="serverType">Тип сервера</param>
        /// <returns></returns>
        public IServer GetServer(ServerType serverType)
        {
            IServer myServer = null;
            for (int i = 0; i < Servers.Count; i++)
            {
                if(Servers[i].ServerType == serverType)
                {
                    myServer = Servers[i];
                    break;
                }
            }

            return myServer;
        }

        // bot netto position
        /// <summary>
        /// Обработчик события прихода нового трейда по позициям портфеля
        /// Используется для определения нетто позиции робота
        /// и подписку на получение всех трейдов по инструменту
        /// </summary>
        /// <param name="myTrade">Трейд</param>
        private void NewServer_NewMyTradeEvent(MyTrade myTrade)
        {
            ServerType myServerType = ServerType.None;        // сервер, по которому прошла сделка

            // проверяем, что трейд относится к нашему роботу (роботов может быть несколько)
            // трейд должен относится к одному из ордеров робота
            // ищем в списке ордеров робота ордер с таким же идентификатором бирже как у трейда
            bool isMyOrder = false;
            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].NumberMarket == myTrade.NumberOrderParent)
                {
                    isMyOrder = true;
                    myServerType = _orders[i].ServerType;
                    break;
                }
            }
            // если не нашли, то выходим из метода
            if (isMyOrder == false)
            {
                return;
            }

            // получаем сервер
            IServer myServer = GetServer(myServerType);

            if(myServer == null)
            {
                return;
            }

            // получаем инструмент из сервера
            Security security = myServer.GetSecurityForName(myTrade.SecurityNameCode);

            // ищем позицию трейда в списке позиций робота
            // если нашли, то загружаем трейд в позицию и выходим из цикла
            // иначе создаем новую позицию в роботе
            for (int i = 0; i < _positions.Count; i++)
            {
                if (_positions[i].SecurityName == myTrade.SecurityNameCode)
                {
                    _positions[i].LoadNewTrade(myTrade, security, Stop, Profit);
                    return;
                }
            }

            // создаем новую позицию
            MyPosition newPos = new MyPosition();

            // сохраняем в позицию инструмент и название инструмента
            newPos.SecurityName = myTrade.SecurityNameCode;
            newPos.Security = security;

            // добавляем трейд в позицию
            newPos.LoadNewTrade(myTrade, security, Stop, Profit);

            // добавляем позицию в список позиций робота
            _positions.Add(newPos);

            // подписываемся на получение всех трейдов по инструменту
            myServer.StartThisSecurity(myTrade.SecurityNameCode, new TimeFrameBuilder());
        }

        // stop and profit
        /// <summary>
        /// Обработчик события прихода нового трейда в таблице обезличенных сделок по инструменту.
        /// Новый трейд - это последний трейд в списки
        /// Используется для проверки стопов и профитов
        /// Используется для проверки времени жизни ордеров по инструменту
        /// </summary>
        /// <param name="trades">Список трейдов</param>
        private void NewServer_NewTradeEvent(List<Trade> trades)
        {
            // проверяем, что включено сопровождение позиций робота
            if (!IsOnPositionSupport)
            {
                return;
            }
            
            // проверяем время жизни ордеров по инструменту
            CheckOrdersLifeTime(trades[trades.Count - 1].Time);

            // ищем позицию робота, к которой относится новый трейд
            MyPosition position = null;
            for (int i = 0; i < _positions.Count; i++)
            {
                if (_positions[i].SumVolume != 0 &&
                    _positions[i].MyTrades[0].SecurityNameCode == trades[0].SecurityNameCode)
                {
                    position = _positions[i];
                    break;
                }
            }
            // если позицию не нашли, то выходим из метода
            if (position == null)
            {
                return;
            }

            // если позицию закрывается, то ничего не делаем
            if (position.Status == MyPosStatus.Closing)
            {
                return;
            }

            // в качестве последней цены по инструменту берем цену из последнего трейда
            decimal lastPrice = trades[trades.Count - 1].Price;

            if (position.SumVolume > 0)
            { // long
                if (lastPrice >= position.Profit)
                {
                    ClosePosition(position, lastPrice);
                }
                else if (lastPrice <= position.Stop)
                {
                    ClosePosition(position, lastPrice);
                }
            }
            else if (position.SumVolume < 0)
            { // short
                if (lastPrice <= position.Profit)
                {
                    ClosePosition(position, lastPrice);
                }
                else if (lastPrice >= position.Stop)
                {
                    ClosePosition(position, lastPrice);
                }
            }
        }
        
        /// <summary>
        /// Закрытие позиции
        /// </summary>
        /// <param name="position">Позиция</param>
        /// <param name="price">Цена закрытия</param>
        private void ClosePosition(MyPosition position, decimal price)
        {
            //public void SendOrder(ServerType server, string portfolio, string security, decimal price, decimal volume, Side orderSide)

            ServerType server = ServerType.None;
            string portfolioNum = "";
            string security = "";
            decimal volume;

            position.Status = MyPosStatus.Closing;

            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].NumberMarket == position.MyTrades[0].NumberOrderParent)
                {
                    server = _orders[i].ServerType;
                    portfolioNum = _orders[i].PortfolioNumber;
                    break;
                }
            }

            security = position.Security.Name;
            volume = Math.Abs(position.SumVolume);

            if (position.SumVolume > 0)
            {
                SendOrder(server, portfolioNum, security, price, volume, Side.Sell);
            }
            else if (position.SumVolume < 0)
            {
                SendOrder(server, portfolioNum, security, price, volume, Side.Buy);
            }
        }

        // close orders on lifetime
        /// <summary>
        /// Проверка ордеров на время жизни
        /// </summary>
        /// <param name="time">текущее время</param>
        private void  CheckOrdersLifeTime(DateTime time)
        {
            if(_lastCheckTime == time)
            {
                return;
            }
            _lastCheckTime = time;

            for (int i = 0; i < _orders.Count; i++)
            {
                if(_orders[i].State == OrderStateType.Activ &&
                    _orders[i].TimeCallBack.AddSeconds(OrderLifeTime) < time)
                {
                    RejectOrder(_orders[i]);
                    _orders[i].State = OrderStateType.Cancel;
                }
            }

        }
        #endregion
    }

    /// <summary>
    /// Класс позиций робота
    /// </summary>
    class MyPosition
    {
        public List<MyTrade> MyTrades = new List<MyTrade>();            // список трейдов по позиции
        public Security Security;                                       // инструмент позиции
        public string SecurityName;                                     // название инструмента
        public decimal PriceEnter;                                      // цена входа в позицию
        public decimal SumVolume;                                       // суммарный объем позиции
        public decimal Stop;                                            // стоплосс
        public decimal Profit;
        public MyPosStatus Status = MyPosStatus.Open;


        public MyPosition()
        {
        }

        /// <summary>
        /// Добавление нового трейда в позицию
        /// </summary>
        /// <param name="myTrade">Новый трейд</param>
        public void LoadNewTrade(MyTrade myTrade, Security security, int stopSteps, int profitSteps)
        {
            // проверяем дубли по трейдов по позиции
            // если такой трейд уже был, то выходим из метода
            for (int i = 0; i < MyTrades.Count; i++)
            {
                if(MyTrades[i].NumberTrade == myTrade.NumberTrade)
                {
                    return;
                }
            }
            
            // добавляем новый трейд в список трейдов позиции
            MyTrades.Add(myTrade);

            PriceEnter = myTrade.Price;

            // изменяем суммарный объем позиции
            decimal volume = 0;
            for (int i = 0; i < MyTrades.Count; i++)
            {
                if(MyTrades[i].Side == Side.Buy)
                {
                    volume += MyTrades[i].Volume;
                }
                else if(MyTrades[i].Side == Side.Sell)
                {
                    volume -= MyTrades[i].Volume;
                }
            }
            SumVolume = volume;

            // считаем стоп и профит
            if(SumVolume > 0)
            {
                Stop = PriceEnter - stopSteps * security.PriceStep;
                Profit = PriceEnter + profitSteps * security.PriceStep;

            }
            else if(SumVolume < 0)
            {
                Stop = PriceEnter + stopSteps * security.PriceStep;
                Profit = PriceEnter - profitSteps * security.PriceStep;
            }
            else
            {
                // если суммарынй объем позиции равен нулю, то меняем статус позиции
                Status = MyPosStatus.Open;
            }
        }
    }

    public enum MyPosStatus
    {
        Open,
        Closing
    }
}
