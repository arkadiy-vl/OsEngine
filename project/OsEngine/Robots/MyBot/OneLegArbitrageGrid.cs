using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.OsTrader;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.Entity;
using OsEngine.Charts.CandleChart.Indicators;
using System.IO;
using System.Threading;
using System.Windows;

namespace OsEngine.Robots.MyBot
{
    /// <summary>
    /// Робот для одноного арбитража относительно индекса из курса OsEngine - Арбитраж
    /// В данном роботе надо отключать сопровождение позиции
    /// </summary>
    [Bot("OneLegArbitrageGrid")]
    public class OneLegArbitrageGrid : BotPanel
    {
        #region Параметры робота
        // оптимизируемые параметры робота
        private StrategyParameterInt LenghtMA;                  // длина индикатора скользящая средняя MA
        private StrategyParameterInt LenghtIvashovMA;           // длина скользящей средней индикатора IvashovRange 
        private StrategyParameterInt LenghtIvashovAverage;      // длина усреднения индикатора IvashovRange
        public StrategyParameterDecimal Multiply;               // коэффициент для построения канала индекса

        // настроечные параметры робота
        public bool IsOn = false;                               // включение/выключение робота
        public decimal Volume = 1;                              // объем входа
        public int MaxPositionsCount = 10;                      // максимальное количество позиций, которое может открыть робот
        public int PositionsSpread = 10;                         // спред между открываемыми позициями
        public int MaxOrderDistance = 110;                      // максимальное расстояние от края стакана
        public int TradeTimePeriod = 10;                        // временной период торговли робота в секундах

        // последнее значение времени торговли робота
        DateTime _lastTradeTime = DateTime.Now.AddYears(-10);

        // вкладка для торговли и индекса
        BotTabSimple _tab;
        BotTabIndex _tabIndex;

        // индикаторы
        MovingAverage _ma;
        IvashovRange _ivashov;

        // последнее значение индекса и индикаторов
        decimal _lastIndexPrice;
        decimal _lastMA;
        decimal _lastIvashov;

        // имя программы, которая запустила бота
        private StartProgram _startProgram;

        #endregion

        /// <summary>
        /// Конструктор класса робота
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Имя программы, которая запустила робота</param>
        public OneLegArbitrageGrid(string name, StartProgram startProgram) : base(name, startProgram)
        {
            // сохраняем программу, которая запустила робота
            // это может быть тестер, оптимизатор, терминал
            _startProgram = StartProgram;

            // создаем вкладки
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Index);
            _tab = TabsSimple[0];
            _tabIndex = TabsIndex[0];

            // создаем оптимизируемые параметры
            LenghtMA = CreateParameter("LenghtMA", 60, 60, 200, 20);
            LenghtIvashovMA = CreateParameter("LenghtIvashovMA", 100, 60, 200, 20);
            LenghtIvashovAverage = CreateParameter("LenghtIvashovAverage", 100, 60, 200, 20);
            Multiply = CreateParameter("Multiply", 1.0m, 0.6m, 2, 0.2m);

            // создаем индикаторы
            _ma = new MovingAverage(name + "ma", false);
            _ma = (MovingAverage)_tabIndex.CreateCandleIndicator(_ma, "Prime");
            _ma.Lenght = LenghtMA.ValueInt;
            _ma.Save();

            _ivashov = new IvashovRange(name + "ivashov", false);
            _ivashov = (IvashovRange)_tabIndex.CreateCandleIndicator(_ivashov, "Second");
            _ivashov.LenghtAverage = LenghtIvashovAverage.ValueInt;
            _ivashov.LenghtMa = LenghtIvashovMA.ValueInt;
            _ivashov.Save();

            // загружаем настроечные параметры бота
            Load();

            // подписка на событие обновление индекса
            _tab.ServerTimeChangeEvent += _tab_ServerTimeChangeEvent;

            // подписка на сервисные события
            ParametrsChangeByUser += OneLegArbitrage_ParametrsChangeByUser;
            DeleteEvent += OneLegArbitrage_DeleteEvent;
        }

        #region Сервисные методы
        /// <summary>
        /// Сервисный метод получения имени стратегии робота
        /// </summary>
        /// <returns></returns>
        public override string GetNameStrategyType()
        {
            return "OneLegArbitrageGrid";
        }

        /// <summary>
        /// Сервисный метод вывода окна настроек робота
        /// </summary>
        public override void ShowIndividualSettingsDialog()
        {
            OneLegArbitrageGridUi ui = new OneLegArbitrageGridUi(this);
            ui.ShowDialog();
        }

        /// <summary>
        /// save settings
        /// сохранить настройки
        /// </summary>
        public void Save()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt", false))
                {
                    writer.WriteLine(IsOn);
                    writer.WriteLine(Volume);
                    writer.WriteLine(MaxPositionsCount);
                    writer.WriteLine(PositionsSpread);
                    writer.WriteLine(MaxOrderDistance);
                    writer.WriteLine(TradeTimePeriod);

                    writer.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не могу сохранить настройки робота");
            }
        }

        /// <summary>
        /// load settings
        /// загрузить настройки
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
                    IsOn = Convert.ToBoolean(reader.ReadLine());
                    Volume = Convert.ToDecimal(reader.ReadLine());
                    MaxPositionsCount = Convert.ToInt32(reader.ReadLine());
                    PositionsSpread = Convert.ToInt32(reader.ReadLine());
                    MaxOrderDistance = Convert.ToInt32(reader.ReadLine());
                    TradeTimePeriod = Convert.ToInt32(reader.ReadLine());

                    reader.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не могу загрузить настройки робота");
            }
        }

        /// <summary>
        /// Обработчик события изменения пользователем настроечных параметров робота 
        /// </summary>
        private void OneLegArbitrage_ParametrsChangeByUser()
        {
            if (_ma.Lenght != LenghtMA.ValueInt)
            {
                _ma.Lenght = LenghtMA.ValueInt;
                _ma.Reload();
            }

            if (_ivashov.LenghtAverage != LenghtIvashovAverage.ValueInt ||
                _ivashov.LenghtMa != LenghtIvashovMA.ValueInt)
            {
                _ivashov.LenghtAverage = LenghtIvashovAverage.ValueInt;
                _ivashov.LenghtMa = LenghtIvashovMA.ValueInt;
                _ivashov.Reload();
            }
        }

        /// <summary>
        /// Обработчик события удаления пользователем робота
        /// </summary>
        private void OneLegArbitrage_DeleteEvent()
        {
            if(File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt");
            }
        }
        #endregion

        #region Основная торговая логика
        private void _tab_ServerTimeChangeEvent(DateTime time)
        {
            // проверяем, что робот включен
            if (IsOn == false)
            {
                return;
            }

            // проверяем, что вкладка индекса и вкладка для торговли подключены
            if (_tabIndex.IsConnected == false || _tab.IsConnected == false)
            {
                return;
            }

            // проверяем наличие свечей в индексе и вкладке для торговли
            // и их достаточность для расчета индикаторов
            List<Candle> candlesIndex = _tabIndex.Candles;
            List<Candle> candlesTab = _tab.CandlesFinishedOnly;

            if (candlesIndex == null ||
                candlesIndex.Count < _ma.Lenght + 2 ||
                candlesIndex.Count < _ivashov.LenghtMa + 30 ||
                candlesIndex.Count < _ivashov.LenghtAverage + 30 ||
                candlesTab == null || candlesTab.Count < 1)
            {
                return;
            }

            // запускаем торговую логику только через периоды времени _tradeTimePeriod 
            if (_lastTradeTime.AddSeconds(TradeTimePeriod) > time)
            {
                return;
            }
            _lastTradeTime = time;

            // сохраняем последнее значение индекса и индикаторов (для упрощения кода)
            _lastIndexPrice = candlesIndex[candlesIndex.Count - 1].Close;
            _lastMA = _ma.Values[_ma.Values.Count - 1];
            _lastIvashov = _ivashov.Values[_ivashov.Values.Count - 1];

            // определяем текущую фазу рынка
            MarketFaze currentMarketFaze = GetMarketFaze();

            if(currentMarketFaze == MarketFaze.Up)
            {
                TryOpenShortPositions();
                TryCloseLongPositions();
            }
            else if(currentMarketFaze == MarketFaze.Down)
            {
                TryOpenLongPositions();
                TryCloseShortPositions();
            }
            else if (currentMarketFaze == MarketFaze.Neutral)
            {
                CheckClosingPositions();
            }

            CheckDistanceToOrder();

        }

        private void TryOpenShortPositions()
        {
            List<Position> positions = _tab.PositionsOpenAll;

            int curPosCount = positions.Count;
            decimal curPricePosition = _tab.PriceBestAsk;

            for(int i = 0; i < MaxPositionsCount - curPosCount; i++)
            {
                if(positions.Find(pos=>pos.EntryPrice == curPricePosition) != null)
                {
                    curPricePosition += PositionsSpread * _tab.Securiti.PriceStep;
                    i--;
                    continue;
                }
                _tab.SellAtLimit(Volume, curPricePosition);
                curPricePosition += PositionsSpread * _tab.Securiti.PriceStep;
            }
        }

        private void TryOpenLongPositions()
        {
            List<Position> positions = _tab.PositionsOpenAll;

            int curPosCount = positions.Count;
            decimal curPricePosition = _tab.PriceBestBid;

            for (int i = 0; i < MaxPositionsCount - curPosCount; i++)
            {
                if (positions.Find(pos => pos.EntryPrice == curPricePosition) != null)
                {
                    curPricePosition -= PositionsSpread * _tab.Securiti.PriceStep;
                    i--;
                    continue;
                }
                _tab.BuyAtLimit(Volume, curPricePosition);
                curPricePosition -= PositionsSpread * _tab.Securiti.PriceStep;
            }
        }

        private void TryCloseShortPositions()
        {
            // получаем все позиции шорт
            List<Position> positions = _tab.PositionOpenShort;

            for (int i = 0; i < positions.Count; i++)
            {
                // проверяем, что позиция уже не закрывается
                if(positions[i].CloseActiv)
                {
                    continue;
                }
                // выставляем лимитную заявку на закрытие позиции шорт (покупаем)
                _tab.CloseAtLimit(positions[i], _tab.PriceBestAsk, positions[i].OpenVolume);
            }
        }               
                        
        private void TryCloseLongPositions()
        {
            // получаем все позиции лонг
            List<Position> positions = _tab.PositionOpenLong;

            for (int i = 0; i < positions.Count; i++)
            {
                // проверяем, что позиция уже не закрывается
                if (positions[i].CloseActiv)
                {
                    continue;
                }
                // выставляем лимитную заявку на закрытие позиции лонг (продаем)
                _tab.CloseAtLimit(positions[i], _tab.PriceBestBid, positions[i].OpenVolume);
            }
        }

        private void CheckClosingPositions()
        {
            // не реализовано
        }

        private void CheckDistanceToOrder()
        {
            // минимально допустимая цена для открытия лонг позиции
            decimal downLongPrice = _tab.PriceBestBid - MaxOrderDistance * _tab.Securiti.PriceStep;

            // максимально допустимая цена для открытия шорт позиции
            decimal upShortPrice = _tab.PriceBestAsk + MaxOrderDistance * _tab.Securiti.PriceStep;
            
            // получаем все открытые позиции
            List<Position> positions = _tab.PositionsOpenAll;
            for (int i = 0; i < positions.Count; i++)
            {
                // если у позиции есть ордер на открытие и исполненный объем ордера равен нулю
                // и цена ордера вышла за предельную цену, то отзываем ордер
                if (positions[i].OpenActiv &&
                    positions[i].OpenOrders[positions[i].OpenOrders.Count-1].VolumeExecute == 0)
                {
                    if(positions[i].Direction == Side.Buy &&
                        positions[i].OpenOrders[positions[i].OpenOrders.Count-1].Price < downLongPrice)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                    else if (positions[i].Direction == Side.Sell &&
                       positions[i].OpenOrders[positions[i].OpenOrders.Count - 1].Price > upShortPrice)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                }
                // если у позициии есть ордер на закрытие и исполненный объем ордера равен нулю
                // и цена ордера вышла за предельную цену, то отзываем ордер 
                if (positions[i].CloseActiv &&
                    positions[i].CloseOrders[positions[i].CloseOrders.Count - 1].VolumeExecute == 0)
                {
                    if (positions[i].Direction == Side.Buy &&
                       positions[i].CloseOrders[positions[i].CloseOrders.Count - 1].Price > upShortPrice)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                    else if (positions[i].Direction == Side.Sell &&
                       positions[i].CloseOrders[positions[i].CloseOrders.Count - 1].Price < downLongPrice)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Получить текущую фазу рынка (Up, Down, Neutral)
        /// </summary>
        /// <returns></returns>
        private MarketFaze GetMarketFaze()
        {
            MarketFaze currentMarketFaze = MarketFaze.Neutral;

            if(_lastIndexPrice > _lastMA + _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Up;
            }
            else if(_lastIndexPrice < _lastMA - _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Down;
            }
            else
            {
                currentMarketFaze = MarketFaze.Neutral;
            }

            return currentMarketFaze;
        }
        #endregion
    }
    
    // Фаза рынка
    public enum MarketFaze
    {
        Up,
        Down,
        Neutral
    }

}
