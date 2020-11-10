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
    /// Робот для одноногого арбитража относительно индекса из курса OsEngine - Арбитраж (c моими небольшими изменениями)
    /// Робот торгует через заданные интервалы времени (10 сек), поэтому тестировать надо на тиковых данных.
    /// В данном роботе надо отключать сопровождение позиции, а в тестере надо выставлять большие значения на время отзыва ордеров.
    /// Входы, выходы при выходе спреда за границы канала, построенного от SMA спреда с помощью индикатора IvashovRange.
    /// Робот выставляет сетку ордеров в соответствии с настройками и в зависимости от текущей фазы рынка.
    /// Если индекс выше канала (фазы рынка "Upper"), то выставляем сетку ордеров на покупку инструмента.
    /// Если индекс ниже канала (фазы рынка "Lower"), то выставляем сетку ордеров на продажу инструмента.
    /// Если ордер не исполнился и цена ушла от него, то ордер снимается.
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
        public decimal Volume = 0.01m;                          // объем входа
        public int MaxPositionsCount = 10;                      // максимальное количество позиций, которое может открыть робот
        public int PositionsSpread = 10;                        // спред между открываемыми позициями
        public int MaxOrderDistance = 110;                      // максимальное расстояние от края стакана
        public int TradeTimePeriod = 10;                        // временной период торговли робота в секундах
        public int Slippage = 10;                               // проскальзывание, используется только при закрытии позиций
        public decimal PriceStep = 0;                           // шаг цены

        
        DateTime _lastTradeTime = DateTime.Now.AddYears(-10);   // последнее значение времени торговли робота
        private StartProgram _startProgram;                     // имя программы, которая запустила бота

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

        // событие изменение фазы рынка
        public event Action<MarketFaze> MarketFazeChangeEvent;
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
            ui.Show();
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
                    writer.WriteLine(Slippage);

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
                    Slippage = Convert.ToInt32(reader.ReadLine());

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
            // запускаем торговую логику только через периоды времени _tradeTimePeriod 
            if (_lastTradeTime.AddSeconds(TradeTimePeriod) > time)
            {
                return;
            }
            _lastTradeTime = time;

            // проверяем, что вкладка индекса и вкладка для торговли подключены
            if (_tabIndex.IsConnected == false || _tab.IsConnected == false)
            {
                return;
            }

            // проверка на включение робота выполняется позже
            // для возможности вывода текущей фазы рынка в окно настроечных параметров

            // сохраняем шаг цены для вывода в окно настроечных параметров
            PriceStep = _tab.Securiti.PriceStep;

            // проверяем наличие свечей в индексе и вкладке для торговли
            // и их достаточность для расчета индикаторов
            List<Candle> candlesIndex = _tabIndex.Candles;
            List<Candle> candlesTab = _tab.CandlesFinishedOnly;

            if (candlesIndex == null ||
                candlesIndex.Count < _ma.Lenght + 30 ||
                candlesIndex.Count < _ivashov.LenghtMa + 30 ||
                candlesIndex.Count < _ivashov.LenghtAverage + 30 ||
                candlesTab == null ||
                candlesTab.Count < _ma.Lenght + 30 ||
                _ma.Values.Count < 30 ||
                _ivashov.Values.Count < 30)
            {
                return;
            }

            // сохраняем последнее значение индекса и индикаторов (для упрощения кода)
            _lastIndexPrice = candlesIndex[candlesIndex.Count - 1].Close;
            _lastMA = _ma.Values[_ma.Values.Count - 1];
            _lastIvashov = _ivashov.Values[_ivashov.Values.Count - 1];

            // определяем текущую фазу рынка
            MarketFaze currentMarketFaze = GetMarketFaze();

            // если кто-то подписан на событие изменения фазы рынка, то выдаем ему текущую фазу рынка           
            if (MarketFazeChangeEvent != null)
            {
                MarketFazeChangeEvent(currentMarketFaze);
            }

            // если фазы рынка не определилась, то ничего не делаем
            if (currentMarketFaze == MarketFaze.Nothing)
            {
                return;
            }

            // проверяем, что робот включен
            if (IsOn == false)
            {
                return;
            }

            // в зависимости от фазы рынка пробуем войти в позицию
            if (currentMarketFaze == MarketFaze.Upper)
            {
                TryOpenLongPositions();
                TryCloseShortPositions();
            }
            else if(currentMarketFaze == MarketFaze.Lower)
            {
                TryOpenShortPositions();
                TryCloseLongPositions();
            }
            else if (currentMarketFaze == MarketFaze.Up || currentMarketFaze == MarketFaze.Low)
            {
                CheckClosingPositions();
            }

            CheckDistanceToOrder();
        }

        private void TryOpenShortPositions()
        {
            // получаем все открытые или открывающиеся позиции
            List<Position> positions = _tab.PositionsOpenAll;
            int curPosCount = positions.Count;
            
            // цена, от которой будем выставлять сетку ордеров
            // сетка ордеров на продажу выставляется выше этой цены
            decimal curPricePosition = _tab.PriceBestAsk;

            // выставление сетки ордеров лимитными заявками
            for(int i = 0; i < MaxPositionsCount - curPosCount; i++)
            {
                // если уже есть позиция с такой же ценой открытия, то не выставляем ордер
                if (positions.Find(pos=>pos.EntryPrice == curPricePosition) != null)
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
            // получаем все открытые или открывающиеся позиции
            List<Position> positions = _tab.PositionsOpenAll;
            int curPosCount = positions.Count;

            // цена, от которой будем выставлять сетку ордеров
            // сетка ордеров на покупку выставляется ниже этой цены
            decimal curPricePosition = _tab.PriceBestBid;

            // выставление сетки ордеров лимитными заявками
            for (int i = 0; i < MaxPositionsCount - curPosCount; i++)
            {
                // если уже есть позиция с такой ценой, то не выставляем ордер
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
                // если позиция уже закрывается, то ничего не делаем
                if(positions[i].CloseActiv)
                {
                    continue;
                }
                // выставляем лимитную заявку на закрытие позиции шорт (покупаем)
                // дополнительно добавил проскальзывание
                _tab.CloseAtLimit(positions[i], 
                    _tab.PriceBestAsk + Slippage * _tab.Securiti.PriceStep,
                    positions[i].OpenVolume);
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
                // дополнительно добавил проскальзывание
                _tab.CloseAtLimit(positions[i],
                    _tab.PriceBestBid - Slippage * _tab.Securiti.PriceStep,
                    positions[i].OpenVolume);
            }
        }

        private void CheckClosingPositions()
        {
            // не реализовано
        }

        private void CheckDistanceToOrder()
        {
            // минимально допустимая цена для открытия лонг позиции
            decimal downPriceLong = _tab.PriceBestBid - MaxOrderDistance * _tab.Securiti.PriceStep;

            // максимально допустимая цена для открытия шорт позиции
            decimal upPriceShort = _tab.PriceBestAsk + MaxOrderDistance * _tab.Securiti.PriceStep;
            
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
                        positions[i].OpenOrders[positions[i].OpenOrders.Count-1].Price < downPriceLong)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                    else if (positions[i].Direction == Side.Sell &&
                       positions[i].OpenOrders[positions[i].OpenOrders.Count - 1].Price > upPriceShort)
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
                       positions[i].CloseOrders[positions[i].CloseOrders.Count - 1].Price > upPriceShort)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                    else if (positions[i].Direction == Side.Sell &&
                       positions[i].CloseOrders[positions[i].CloseOrders.Count - 1].Price < downPriceLong)
                    {
                        _tab.CloseAllOrderToPosition(positions[i]);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Получить текущую фазу рынка
        /// </summary>
        /// <returns></returns>
        private MarketFaze GetMarketFaze()
        {
            MarketFaze currentMarketFaze = MarketFaze.Nothing;

            // если индекс выше канала, то фазы рынка "Upper"
            if(_lastIndexPrice > _lastMA + _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Upper;
            }
            // если индекс выше средней, но ниже верхней границы канала, то фаза рынка "Up"
            else if (_lastIndexPrice > _lastMA &&
                     _lastIndexPrice <= _lastMA + _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Up;
            }
            // если индекс ниже средней, но выше нижней границы канала, то фаза рынка "Low"
            else if (_lastIndexPrice <= _lastMA &&
                     _lastIndexPrice >= _lastMA - _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Low;
            }
            // если индекс ниже канала, то фаза рынка "Lower"
            else if(_lastIndexPrice < _lastMA - _lastIvashov * Multiply.ValueDecimal)
            {
                currentMarketFaze = MarketFaze.Lower;
            }
            else
            {
                currentMarketFaze = MarketFaze.Nothing;
            }

            return currentMarketFaze;
        }
        #endregion
    }
    
    // Фаза рынка
    public enum MarketFaze
    {
        Upper,
        Up,
        Low,
        Lower,
        Nothing
    }

}

