using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.OsTrader;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Entity;
using OsEngine.Charts.CandleChart.Indicators;
using System.IO;
using System.Threading;
using System.Windows;

namespace OsEngine.Robots.MyBot
{

    /// <summary>
    /// Робот для одноного арбитража относительно индекса из курса OsEngine - Арбитраж
    /// </summary>
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
        public int MaxPositionsCount = 10;                           // максимальное количество позиций, которое может открыть робот
        public int PositionsSpread = 1;                             // спред между открываемыми позициями
        public int MaxOrderDistance = 15;                            // максимальное расстояние от края стакана

        // временной период торговли робота в секундах 
        int _tradeTimePeriod = 10;

        // последнее значение времени торговли робота
        DateTime _lastTradeTime = DateTime.Now;

        // вкладка для торговли
        BotTabSimple _tab;

        // вкладка для индекса
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

                    reader.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не могу загрузить настройки робота");
            }
        }

        private void OneLegArbitrage_ParametrsChangeByUser()
        {
            
        }

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
            if (_lastTradeTime.AddSeconds(_tradeTimePeriod) > time)
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
            decimal curPricePosition = _tab.PriceBestBid;

            for(int i = 0; i < MaxPositionsCount - curPosCount; i++)
            {

            }

        }

        private void TryOpenLongPositions()
        {

        }

        private void TryCloseShortPositions()
        {               
                        
        }               
                        
        private void TryCloseLongPositions()
        {

        }

        private void CheckClosingPositions()
        {

        }

        private void CheckDistanceToOrder()
        {

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
