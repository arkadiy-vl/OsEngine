using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.Entity;
using OsEngine.Charts.CandleChart.Indicators;
using System.IO;
using System.Windows;


namespace OsEngine.Robots.MyBot
{
    /// <summary>
    /// Робот для парного межбиржевого арбитража из курса OsEngine - Арбитраж (c моими небольшими изменениями)
    /// Торгует при изменении спреда на тиках.
    /// Тестировать надо, либо на тиковых данных, либо в режиме эмулятора на реальных подключениях к биржам.
    /// Входы, выходы при пробитии спредом границ канала, построенного от SMA спреда c помощью индикатора IvashovRange.
    /// Функция выбора каким инструментом входить первым, при этом первый инструмент входит лимитной заявкой,
    /// а второй инструмент - рыночной заявкой.
    /// Функция выбора направления сделок для каждого инструмента при пробитии верхней границы канала,
    /// при этом при пробитии нижней границы направления сделок будет противоположным.
    /// Объем входа в позицию для каждого инструмента задается в настройках.
    /// </summary>
    [Bot("PairArbitrage")]
    public class PairArbitrage : BotPanel
    {
        #region === Параметры робота ===
        // оптимизируемые параметры робота
        private StrategyParameterInt LenghtMA;                  // длина индикатора скользящая средняя MA
        private StrategyParameterInt LenghtIvashovMA;           // длина скользящей средней индикатора IvashovRange 
        private StrategyParameterInt LenghtIvashovAverage;      // длина усреднения индикатора IvashovRange
        public StrategyParameterDecimal Multiply;               // коэффициент для построения канала индекса

        // настроечные параметры робота
        public bool IsOn = false;
        public WhoIsFirst WhoIsFirst = WhoIsFirst.Nobody;       // каким инструментом входим вначале
        public decimal Volume1 = 1;                             // объем входа для инструмента 1
        public decimal Volume2 = 1;                             // объем входа для инструмента 2
        public int Slippage1 = 100;                               // проскальзывание для инструмента 1
        public int Slippage2 = 100;                               // проскальзывание для инструмента 2
        public Side Side1 = Side.Buy;                           // сторона входа для инструмента 1, когда индекс выше канала    
        public Side Side2 = Side.Sell;                          // сторона входа для инструмента 2, когда индекс выше канала

        // вкладки для торговли
        BotTabSimple _tab1;
        BotTabSimple _tab2;

        // вкладка для индекса
        BotTabIndex _tabIndex;

        // индикаторы
        MovingAverage _ma;
        IvashovRange _ivashov;

        // последнее значение индекса и индикаторов
        decimal _lastIndex;
        decimal _lastMA;
        decimal _lastIvashov;

        //Имя программы, которая запустила бота
        private StartProgram _startProgram;

        #endregion

        /// <summary>
        /// Конструктор класса робота
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Имя программы, запустившей робота</param>
        public PairArbitrage(string name, StartProgram startProgram) : base(name, startProgram)
        {
            // сохраняем программу, которая запустила робота
            // это может быть тестер, оптимизатор, терминал
            _startProgram = StartProgram;

            // создаем вкладки
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Index);
            _tab1 = TabsSimple[0];
            _tab2 = TabsSimple[1];
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
            _tabIndex.SpreadChangeEvent += _tabIndex_SpreadChangeEvent;

            // подписка на событие успешного открытия сделки по вкладкам
            _tab1.PositionOpeningSuccesEvent += _tab1_PositionOpeningSuccesEvent;
            _tab2.PositionOpeningSuccesEvent += _tab2_PositionOpeningSuccesEvent;

            // подписка на сервисные события
            ParametrsChangeByUser += PairArbitrage_ParametrsChangeByUser;
            DeleteEvent += Strategy_DeleteEvent;
        }

        #region === Сервисные методы ===
        /// <summary>
        /// Сервисный метод, вызываемый при изменении оптимизируемых параметров робота
        /// </summary>
        private void PairArbitrage_ParametrsChangeByUser()
        {
            if (_ma.Lenght != LenghtMA.ValueInt)
            {
                _ma.Lenght = LenghtMA.ValueInt;
                _ma.Reload();
            }

            if (_ivashov.LenghtMa != LenghtIvashovMA.ValueInt ||
                _ivashov.LenghtAverage != LenghtIvashovAverage.ValueInt)
            {
                _ivashov.LenghtMa = LenghtIvashovMA.ValueInt;
                _ivashov.LenghtAverage = LenghtIvashovAverage.ValueInt;
                _ivashov.Reload();
            }
        }

        /// <summary>
        /// Сервисный метод получения имени стратегии робота
        /// </summary>
        /// <returns></returns>
        public override string GetNameStrategyType()
        {
            return "PairArbitrage";
        }

        /// <summary>
        /// Сервисный метод вывода окна настроек робота
        /// </summary>
        public override void ShowIndividualSettingsDialog()
        {
            PairArbitrageUi ui = new PairArbitrageUi(this);
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
                    writer.WriteLine(WhoIsFirst);
                    writer.WriteLine(Volume1);
                    writer.WriteLine(Volume2);
                    writer.WriteLine(Slippage1);
                    writer.WriteLine(Slippage2);
                    writer.WriteLine(Side1);
                    writer.WriteLine(Side2);

                    writer.Close();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Не могу сохранить настройки робота.\n {e.Message}");

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
                    Enum.TryParse(reader.ReadLine(), out WhoIsFirst);
                    Volume1 = Convert.ToDecimal(reader.ReadLine());
                    Volume2 = Convert.ToDecimal(reader.ReadLine());
                    Slippage1 = Convert.ToInt32(reader.ReadLine());
                    Slippage2 = Convert.ToInt32(reader.ReadLine());
                    Enum.TryParse(reader.ReadLine(), out Side1);
                    Enum.TryParse(reader.ReadLine(), out Side2);

                    reader.Close();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Не могу загрузить настройки робота.\n {e.Message}");
            }
        }

        /// <summary>
        /// delete save file
        /// удаление файла с сохранением
        /// </summary>
        private void Strategy_DeleteEvent()
        {
            if (File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt");
            }
        }

        #endregion

        #region === Основная торговая логика ===
        /// <summary>
        /// Обработчик изменения спреда
        /// </summary>
        /// <param name="candlesIndex">Свечи индекса</param>
        private void _tabIndex_SpreadChangeEvent(List<Candle> candlesIndex)
        {
            // проверяем, что робот включен и вкладки для торговли инструментов подключены
            if (IsOn == false || _tab1.IsConnected == false || _tab2.IsConnected == false)
            {
                return;
            }

            // проверяем наличие свечей в индексе и их достаточность для расчета индикаторов
            if (candlesIndex == null ||
                candlesIndex.Count < _ma.Lenght + 2 ||
                candlesIndex.Count < _ivashov.LenghtMa + 30 ||
                candlesIndex.Count < _ivashov.LenghtAverage + 30)
            {
                return;
            }

            // сохраняем последнее значение индекса и индикаторов (для упрощения кода)
            _lastIndex = candlesIndex[candlesIndex.Count - 1].Close;
            _lastMA = _ma.Values[_ma.Values.Count - 1];
            _lastIvashov = _ivashov.Values[_ivashov.Values.Count - 1];

            // берем все открытые позиции в обоих вкладках для торговли
            List<Position> positionsTab1 = _tab1.PositionsOpenAll;
            List<Position> positionsTab2 = _tab2.PositionsOpenAll;

            // если есть хоть одна открытая позиция,
            // то проверяем условия закрытия позиций,
            if (positionsTab1.Count != 0 || positionsTab2.Count != 0)
            {
                LogicToClose(positionsTab1, positionsTab2);
            }
            // иначе проверяем условия открытия позиций
            else
            {
                LogicToOpen();
            }
        }

        /// <summary>
        /// Логика открытия позиций
        /// </summary>
        /// <param name="candles">Свечи индекса</param>
        private void LogicToOpen()
        {
            // условие выхода индекса (спреда) за верхнюю границу канала
            if (_lastIndex > _lastMA + _lastIvashov * Multiply.ValueDecimal)
            {
                // проверяем, может ли инструмент 1 открываться первым
                if (WhoIsFirst == WhoIsFirst.First || WhoIsFirst == WhoIsFirst.Nobody)
                {
                    // в зависимости от выбранной стороны по инструменту 1 выставляем лимитную заявку по инструменту 1
                    if (Side1 == Side.Buy)
                    {
                        _tab1.BuyAtLimit(Volume1, _tab1.PriceBestAsk + Slippage1 * _tab1.Securiti.PriceStep);
                    }
                    else if (Side1 == Side.Sell)
                    {
                        _tab1.SellAtLimit(Volume1, _tab1.PriceBestBid - Slippage1 * _tab1.Securiti.PriceStep);
                    }
                }

                // проверяем, может ли инструмент 2 открываться первым
                if (WhoIsFirst == WhoIsFirst.Second || WhoIsFirst == WhoIsFirst.Nobody)
                {
                    // в зависимости от выбранной стороны по инструменту 2 выставляем лимитную заявку по инструменту 2
                    if (Side2 == Side.Buy)
                    {
                        _tab2.BuyAtLimit(Volume2, _tab2.PriceBestAsk + Slippage2 * _tab2.Securiti.PriceStep);
                    }
                    else if (Side2 == Side.Sell)
                    {
                        _tab2.SellAtLimit(Volume2, _tab2.PriceBestBid - Slippage2 * _tab2.Securiti.PriceStep);
                    }
                }
            }
            // условие выхода индекса (спреда) за нижнюю границу канала
            else if (_lastIndex  < _lastMA - _lastIvashov * Multiply.ValueDecimal)
            {
                // проверяем, может ли инструмент 1 открываться первым
                if (WhoIsFirst == WhoIsFirst.First || WhoIsFirst == WhoIsFirst.Nobody)
                {
                    // в зависимости от выбранной стороны по инструменту 1 выставляем лимитную заявку по инструменту 1
                    if (Side1 == Side.Sell)
                    {
                        _tab1.BuyAtLimit(Volume1, _tab1.PriceBestAsk + Slippage1 * _tab1.Securiti.PriceStep);
                    }
                    else if (Side1 == Side.Buy)
                    {
                        _tab1.SellAtLimit(Volume1, _tab1.PriceBestBid - Slippage1 * _tab1.Securiti.PriceStep);
                    }
                }

                // проверяем, может ли инструмент 2 открываться первым
                if (WhoIsFirst == WhoIsFirst.Second || WhoIsFirst == WhoIsFirst.Nobody)
                {
                    // в зависимости от выбранной стороны по инструменту 2 выставляем лимитную заявку по инструменту 2
                    if (Side2 == Side.Sell)
                    {
                        _tab2.BuyAtLimit(Volume2, _tab2.PriceBestAsk + Slippage2 * _tab2.Securiti.PriceStep);
                    }
                    else if (Side2 == Side.Buy)
                    {
                        _tab2.SellAtLimit(Volume2, _tab2.PriceBestBid - Slippage2 * _tab2.Securiti.PriceStep);
                    }
                }
            }
        }

        /// <summary>
        /// Логика закрытия позиций
        /// </summary>
        /// <param name="candles">Свечи индекса</param>
        /// <param name="positions1">Открытые позиции по инструменту 1</param>
        /// <param name="positions2">Открытые позиции по инструменту 2</param>
        private void LogicToClose(List<Position> positions1, List<Position> positions2)
        {
            // если позиции по инструментам не в состоянии Open, то ничего не делаем
            // Проблема. Если по какой-то причине открылись только одной ногой, то при этом условии мы никогда не закроемся.
            /*
            if (positions1.Count != 0 && positions1[0].State != PositionStateType.Open ||
                positions2.Count != 0 && positions2[0].State != PositionStateType.Open)
            {
                return;
            }
            */

            // закрытие позиций, которые открылись, когда индекс (спред) был ниже канала 
            if (_lastIndex > _lastMA + _lastIvashov * Multiply.ValueDecimal)
            {
                if (positions1.Count != 0 &&
                    positions1[0].State == PositionStateType.Open &&
                    positions1[0].Direction != Side1)
                {
                    if (positions1[0].Direction == Side.Buy)
                    {
                        _tab1.CloseAtLimit(positions1[0],
                            _tab1.PriceBestBid - Slippage1 * _tab1.Securiti.PriceStep,
                            positions1[0].OpenVolume);
                    }
                    else if (positions1[0].Direction == Side.Sell)
                    {
                        _tab1.CloseAtLimit(positions1[0],
                            _tab1.PriceBestAsk + Slippage1 * _tab1.Securiti.PriceStep,
                            positions1[0].OpenVolume);
                    }
                }

                if (positions2.Count != 0 &&
                    positions2[0].State == PositionStateType.Open &&
                    positions2[0].Direction != Side2)
                {
                    if (positions2[0].Direction == Side.Buy)
                    {
                        _tab2.CloseAtLimit(positions2[0],
                            _tab2.PriceBestBid - Slippage2 * _tab2.Securiti.PriceStep,
                            positions2[0].OpenVolume);
                    }
                    else if (positions2[0].Direction == Side.Sell)
                    {
                        _tab2.CloseAtLimit(positions2[0],
                            _tab2.PriceBestAsk + Slippage2 * _tab2.Securiti.PriceStep,
                            positions2[0].OpenVolume);
                    }
                }

            }
            // закрытие позиций, которые открылись, когда индекс (спред) был выше канала
            else if (_lastIndex < _lastMA - _lastIvashov * Multiply.ValueDecimal)
            {
                if (positions1.Count != 0 &&
                    positions1[0].State == PositionStateType.Open &&
                    positions1[0].Direction == Side1)
                {
                    if (positions1[0].Direction == Side.Buy)
                    {
                        _tab1.CloseAtLimit(positions1[0],
                            _tab1.PriceBestBid - Slippage1 * _tab1.Securiti.PriceStep,
                            positions1[0].OpenVolume);
                    }
                    else if (positions1[0].Direction == Side.Sell)
                    {
                        _tab1.CloseAtLimit(positions1[0],
                            _tab1.PriceBestAsk + Slippage1 * _tab1.Securiti.PriceStep,
                            positions1[0].OpenVolume);
                    }
                }

                if (positions2.Count != 0 &&
                    positions2[0].State == PositionStateType.Open &&
                    positions2[0].Direction == Side2)
                {
                    if (positions2[0].Direction == Side.Buy)
                    {
                        _tab2.CloseAtLimit(positions2[0],
                            _tab2.PriceBestBid - Slippage2 * _tab2.Securiti.PriceStep,
                            positions2[0].OpenVolume);
                    }
                    else if (positions2[0].Direction == Side.Sell)
                    {
                        _tab2.CloseAtLimit(positions2[0],
                            _tab2.PriceBestAsk + Slippage2 * _tab2.Securiti.PriceStep,
                            positions2[0].OpenVolume);
                    }
                }
            }

        }

        /// <summary>
        /// Обработчик успешного открытия позиции по инструменту 1
        /// </summary>
        /// <param name="position1">Успешно открытая позиция по инструменту 1</param>
        private void _tab1_PositionOpeningSuccesEvent(Position position1)
        {
            if(WhoIsFirst != WhoIsFirst.First)
            {
                return;
            }

            // в зависимости от направления открытой позиции по инструменту 1
            // выставляем противоположную рыночную заявку по инструменту 2
            if (position1.Direction == Side.Sell)
            {
                _tab2.BuyAtMarket(Volume2);
            }
            else if (position1.Direction == Side.Buy)
            {
                _tab2.SellAtMarket(Volume2);
            }
        }

        /// <summary>
        /// Обработчик успешного открытия позиции по инструменту 2
        /// </summary>
        /// <param name="position2">Успешно открытая позиция по инструменту 2</param>
        private void _tab2_PositionOpeningSuccesEvent(Position position2)
        {
            if (WhoIsFirst != WhoIsFirst.Second)
            {
                return;
            }
            // в зависимости от направления открытой позиции  по инструменту 2
            // выставляем противоположную рыночную заявку по инструменту 1
            if (position2.Direction == Side.Sell)
            {
                _tab1.BuyAtMarket(Volume1);
            }
            else if (position2.Direction == Side.Buy)
            {
                _tab1.SellAtMarket(Volume1);
            }
        }

        #endregion
    }

    /// <summary>
    /// Перечислие "Кто первый",
    /// используется для задания порядка входа в позиции по двум инструментам
    /// </summary>
    public enum WhoIsFirst
    {
        First,
        Second,
        Nobody
    }
}
