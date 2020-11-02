using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Logging;

namespace OneLegArbitrageMy
{
    /// <summary>
    /// Робот для одноногого арбитража относительно индекса.
    /// Робот торгует на завершении свечи.
    /// Выбор вариантов построения канала индекса (спреда) ChannelIndicator: "SMA + ATR", "Bollinger".
    /// Вход в позицию при выходе спреда за границы канала.
    /// Дополнительные входы в позицию при дальшейшем отклонении индекса на величину DeviationIndexForAddEnter (в %).
    /// Максимальное количество открываемых позиций в одном направлении задается настроечны  параметром MaxPositionsCount.
    /// Выбор объема входа: фиксированный объем FixedVolume, либо процент от депозита VolumePctOfDeposit (депозит в USDT).
    /// Выбор варианта выхода из позиций MethodOfExit: по центру канала, по противоположной границе канала.
    /// </summary>

    [Bot("OneLegArbitrageMy")]
    public class OneLegArbitrageMy : BotPanel
    {
        #region === Параметры робота ===

        // оптимизируемые параметры робота
        public StrategyParameterInt LenghtMa;                   // длина индикатора MA для построения канала спреда
        public StrategyParameterInt LenghtAtr;                  // длина индикатора ATR для построения канала спреда
        public StrategyParameterDecimal MultiplyAtr;            // коэффициент для построения канала спреда через MA-ATR 
        public StrategyParameterInt LenghtBollinger;            // длина индикатора Bollinger
        public StrategyParameterDecimal DeviationBollinger;     // отклонение индикатора Bollinger

        // настраиваемые параметры робота
        public bool IsOn = false;                   // режим работы бота - включен/выключен
        public bool OnFixedVolume = true;           // режим фиксированного объема входа в позицию
        public decimal FixedVolume = 1;            // объем входа в позицию в процентах от депозита
        public decimal VolumePctOfDeposit = 30;     // объем входа в позицию в процентах от депозита
        public int Slippage = 200;                  // проскальзывание в шагах цены
        public int VolumeDecimals = 4;              // число знаков после запятой для вычисления объема входа в позицию
        public ChannelIndicator ChannelIndicator = ChannelIndicator.Bollinger; //индикатор, используемый для построения канала индекса (спреда)
        public MethodOfExit MethodOfExit = MethodOfExit.CenterChannel; // когда выходим из позиции: по центру канала или по границе канала
        public int MaxPositionsCount = 2;           // максимальное количество открываемых позиций в одном направлении
        public decimal DeviationIndexForAddEnter = 0.5m;   // отклонение индекса для дополнительного входа в позицию в процентах

        // вкладки робота
        private BotTabIndex _tabIndex;
        private BotTabSimple _tabSec;

        // индикаторы робота
        private MovingAverage _ma;
        private Bollinger _bollinger;
        private Atr _atr;

        //Последние значения цен инструментов и индикаторов
        private decimal _lastIndex;
        private decimal _lastPrice;
        private decimal _lastMa;
        private decimal _lastUpBollinger;
        private decimal _lastDownBollinger;
        private decimal _lastAtr;
        private decimal _lastUpChannel;
        private decimal _lastDownChannel;

        private StartProgram _startProgram;                     // программа, которая запустила робота
        public event Action<MarketFaze> MarketFazeChangeEvent;  // событие изменения фазы рынка

        #endregion

        /// <summary>
        /// Конструктор класса робота
        /// </summary>
        /// <param name="name">имя робота</param>
        /// <param name="startProgram">программа, которая запустила робота</param>
        public OneLegArbitrageMy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            //Запоминаем имя программы, которая запустила бота
            //Это может быть тестер, оптимизатор, терминал
            _startProgram = startProgram;

            //Создаем вкладки бота
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Index);

            _tabSec = TabsSimple[0];
            _tabIndex = TabsIndex[0];

            // создаем настроечные параметры робота
            LenghtMa = CreateParameter("Lenght MA", 100, 50, 200, 10);
            LenghtBollinger = CreateParameter("Lenght Bollinger", 100, 50, 200, 10);
            DeviationBollinger = CreateParameter("Deviation Bollinger", 1, 0.5m, 2.5m, 0.5m);
            LenghtAtr = CreateParameter("Lenght ATR", 20, 20, 100, 10);
            MultiplyAtr = CreateParameter("Multiplay ATR", 1, 1, 5, 0.5m);

            // cоздаем индикаторы
            _ma = new MovingAverage(name + "MA", false);
            _ma = (MovingAverage)_tabIndex.CreateCandleIndicator(_ma, "Prime");
            _ma.Lenght = LenghtMa.ValueInt;
            _ma.Save();

            _bollinger = new Bollinger(name + "Bollinger", false);
            _bollinger = (Bollinger)_tabIndex.CreateCandleIndicator(_bollinger, "Prime");
            _bollinger.Lenght = LenghtBollinger.ValueInt;
            _bollinger.Deviation = DeviationBollinger.ValueDecimal;
            _bollinger.Save();

            _atr = new Atr(name + "ATR", false);
            _atr = (Atr)_tabIndex.CreateCandleIndicator(_atr, "Second");
            _atr.Lenght = LenghtAtr.ValueInt;
            _atr.Save();

            // загружаем настроечные параметры робота
            Load();

            // подписываемся на события
            _tabIndex.SpreadChangeEvent += TabIndex_SpreadChangeEvent;
            _tabSec.CandleFinishedEvent += TabSec_CandleFinishedEvent;
            ParametrsChangeByUser += OneLegArbitrage_ParametrsChangeByUser;
            DeleteEvent += OneLegArbitrage_DeleteEvent;
            //todo доработать OneLegArbitrage_DeleteEvent, чтобы удалялись все файлы робота
        }

        #region === Сервисная логика ===
        public override string GetNameStrategyType()
        {
            return "OneLegArbitrageMy";
        }

        public override void ShowIndividualSettingsDialog()
        {
            OneLegArbitrageMyUi ui = new OneLegArbitrageMyUi(this);
            ui.Show();
        }

        /// <summary>
        /// Обработчик события изменения пользователем настроечных параметров робота
        /// </summary>
        private void OneLegArbitrage_ParametrsChangeByUser()
        {
            if (_ma.Lenght != LenghtMa.ValueInt)
            {
                _ma.Lenght = LenghtMa.ValueInt;
                _ma.Reload();
            }

            if (_bollinger.Lenght != LenghtBollinger.ValueInt ||
                _bollinger.Deviation != DeviationBollinger.ValueDecimal)
            {
                _bollinger.Lenght = LenghtBollinger.ValueInt;
                _bollinger.Deviation = DeviationBollinger.ValueDecimal;
                _bollinger.Reload();
            }

            if (_atr.Lenght != LenghtAtr.ValueInt)
            {
                _atr.Lenght = LenghtAtr.ValueInt;
                _atr.Reload();
            }
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
                    writer.WriteLine(OnFixedVolume);
                    writer.WriteLine(FixedVolume);
                    writer.WriteLine(VolumePctOfDeposit);
                    writer.WriteLine(Slippage);
                    writer.WriteLine(VolumeDecimals);
                    writer.WriteLine(ChannelIndicator);
                    writer.WriteLine(MethodOfExit);
                    writer.WriteLine(MaxPositionsCount);
                    writer.WriteLine(DeviationIndexForAddEnter);

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
                    OnFixedVolume = Convert.ToBoolean(reader.ReadLine());
                    FixedVolume = Convert.ToDecimal(reader.ReadLine());
                    VolumePctOfDeposit = Convert.ToInt32(reader.ReadLine());
                    Slippage = Convert.ToInt32(reader.ReadLine());
                    VolumeDecimals = Convert.ToInt32(reader.ReadLine());
                    Enum.TryParse(reader.ReadLine(), out ChannelIndicator);
                    Enum.TryParse(reader.ReadLine(), out MethodOfExit);
                    MaxPositionsCount = Convert.ToInt32(reader.ReadLine());
                    DeviationIndexForAddEnter = Convert.ToDecimal(reader.ReadLine());

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
        private void OneLegArbitrage_DeleteEvent()
        {
            if (File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt");
            }

            if (File.Exists(@"Engine\" + NameStrategyUniq + @"StrategSettings.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"StrategSettings`.txt");
            }
        }

        #endregion

        #region === Синхронизация свечей индекса и торгуемого инструмента ====
        /// <summary>
        /// Обработчик события завершения свечи инструмента для торговли
        /// </summary>
        /// <param name="candlesSec"></param>
        private void TabSec_CandleFinishedEvent(List<Candle> candlesSec)
        {
            // проверяем, что вкладка для индекса подключена
            if (_tabIndex.IsConnected == false)
            {
                return;
            }

            // получаем свечи индекса
            List<Candle> candlesIndex = _tabIndex.Candles;

            // проверяем наличие свечей в индексе и вкладке для торговли
            if (candlesSec == null || candlesSec.Count == 0 ||
                candlesIndex == null || candlesIndex.Count == 0)
            {
                return;
            }

            // синхронизируем свечи индекса и вкладки для торговли по времени
            if (candlesIndex[candlesIndex.Count - 1].TimeStart ==
                candlesSec[candlesSec.Count - 1].TimeStart)
            {
                TradeLogic(candlesIndex, candlesSec);
            }
        }

        private void TabIndex_SpreadChangeEvent(List<Candle> candlesIndex)
        {
            // проверяем, что вкладка для торгуемого инструмента подключена
            if (_tabSec.IsConnected == false)
            {
                return;
            }

            // получаем завершенные свечи торгуемого инструмента
            List<Candle> candlesSec = _tabSec.CandlesFinishedOnly;

            // проверяем, что имется свечи в индексе и вкладке для торгуемого инструмента
            if (candlesIndex == null || candlesIndex.Count == 0 ||
                candlesSec == null || candlesSec.Count == 0)
            {
                return;
            }

            // синхронизируем свечи индекса и свечи торгуемого инструмента по времени
            if (candlesIndex[candlesIndex.Count - 1].TimeStart ==
                candlesSec[candlesSec.Count - 1].TimeStart)
            {
                TradeLogic(candlesIndex, candlesSec);
            }
        }
        #endregion


        #region === Торговая логика ====
        private void TradeLogic(List<Candle> candlesIndex, List<Candle> candlesTab)
        {
            // проверка, что робот включен делается позже,
            // для возможности вывода текущей фазы рынка в окно настроечных параметров
            
            // проверка на достаточность свечей
            if (candlesIndex.Count < _ma.Lenght + 10 ||
                candlesIndex.Count < _bollinger.Lenght + 10 ||
                candlesIndex.Count < _atr.Lenght + 10)
            {
                return;
            }

            // получаем последние значения инструментов и индикаторов
            _lastIndex = candlesIndex[candlesIndex.Count - 1].Close;
            _lastPrice = candlesTab[candlesTab.Count - 1].Close;
            _lastMa = _ma.Values[_ma.Values.Count - 1];
            _lastUpBollinger = _bollinger.ValuesUp[_bollinger.ValuesUp.Count - 1];
            _lastDownBollinger = _bollinger.ValuesDown[_bollinger.ValuesDown.Count - 1];
            _lastAtr = _atr.Values[_atr.Values.Count - 1];

            //Проверка на допустимый диапазон значений цен инструментов
            if (_lastPrice <= 0 || _lastIndex <= 0)
            {
                _tabSec.SetNewLogMessage("TradeLigic: цена или индекс меньше или равны нулю.",
                    LogMessageType.Error);
                return;
            }

            // получаем последние значения канала индекса (спреда)
            GetLastChannel();

            // получаем текущую фазу рынка
            MarketFaze currentMarketFaze = GetMarketFaze();

            // если кто-то подписан на событие изменения фазы рынка,
            // то передаем ему текущую фазу рынка
            if (MarketFazeChangeEvent != null)
            {
                MarketFazeChangeEvent(currentMarketFaze);
            }

            // проверяем, что робот включен
            if (IsOn == false)
            {
                return;
            }

            // проверка условий закрытия позиций
            CheckClosingPositions(currentMarketFaze);

            // в зависимости от фазы рынка проверка условий открытия позиций
            if (currentMarketFaze == MarketFaze.Upper)
            {
                TryOpenLong();
            }
            else if (currentMarketFaze == MarketFaze.Lower)
            {
                TryOpenShort();
            }
        }

        /// <summary>
        /// Получить последние значения канала индекса (спреда)
        /// </summary>
        private void GetLastChannel()
        {
            //Строим канал индекса
            if (ChannelIndicator == ChannelIndicator.Bollinger)
            {
                _lastUpChannel = _lastUpBollinger;
                _lastDownChannel = _lastDownBollinger;
            }
            else if (ChannelIndicator == ChannelIndicator.MA_ATR)
            {
                _lastUpChannel = _lastMa + MultiplyAtr.ValueDecimal * _lastAtr;
                _lastDownChannel = _lastMa - MultiplyAtr.ValueDecimal * _lastAtr;
            }
            else
            {
                _lastUpChannel = _lastUpBollinger;
                _lastDownChannel = _lastDownBollinger;
            }
        }

        /// <summary>
        /// Получить текущую фазу рынка
        /// </summary>
        /// <returns></returns>
        private MarketFaze GetMarketFaze()
        {
            MarketFaze currentMarketFaze;

            if (_lastIndex > _lastUpChannel)
            {
                currentMarketFaze = MarketFaze.Upper;
            }
            else if (_lastIndex > _lastMa && _lastIndex <= _lastUpChannel)
            {
                currentMarketFaze = MarketFaze.Up;
            }
            else if (_lastIndex <= _lastMa && _lastIndex >= _lastDownChannel)
            {
                currentMarketFaze = MarketFaze.Low;
            }
            else if (_lastIndex < _lastDownChannel)
            {
                currentMarketFaze = MarketFaze.Lower;
            }
            else
            {
                currentMarketFaze = MarketFaze.Nothing;
            }

            return currentMarketFaze;
        }

        private void OpenShort()
        {
            decimal pricePosition = _tabSec.PriceBestBid - Slippage * _tabSec.Securiti.PriceStep;
            decimal volumePosition = GetVolume(pricePosition);

            _tabSec.SellAtLimit(volumePosition, pricePosition, _lastIndex.ToString());
        }

        private void TryOpenShort()
        {
            // получаем все позиции short
            List<Position> shortPositions = _tabSec.PositionOpenShort;

            // Если шорт позиций нет, то открываем шорт позицию
            if (shortPositions.Count == 0)
            {
                OpenShort();
            }
            // если есть шорт позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную шорт позицию
            else if (shortPositions.Count < MaxPositionsCount)
            {
                // получаем значение индекса, при котором была открыта предыдущая шорт позиция
                decimal indexForPrevPosition;

                try
                {
                    indexForPrevPosition = Convert.ToDecimal(shortPositions[shortPositions.Count - 1].SignalTypeOpen);
                }
                catch (Exception e)
                {
                    _tabSec.SetNewLogMessage("TryOpenShort: не могу получить значение индекса," +
                                             " при котором была открыта предыдущая шорт позиция", LogMessageType.Error);
                    return;
                }

                // если текущее значение индекса стало меньше на заданную величину,
                // чем индекс для предыдущей открытой позиции, то открываем дополнительную шорт позицию
                if (_lastIndex < indexForPrevPosition * (1 - DeviationIndexForAddEnter / 100.0m))
                {
                    OpenShort();
                }
            }
        }

        private void OpenLong()
        {
            decimal pricePosition = _tabSec.PriceBestAsk + Slippage * _tabSec.Securiti.PriceStep;
            decimal volumePosition = GetVolume(pricePosition);

            _tabSec.BuyAtLimit(volumePosition, pricePosition, _lastIndex.ToString());
        }

        private void TryOpenLong()
        {
            // получаем все позиции лонг
            List<Position> longPositions = _tabSec.PositionOpenLong;

            // Если лонг позиций нет, то открываем лонг позицию
            if (longPositions.Count == 0)
            {
                OpenLong();
            }
            // если есть лонг позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную лонг позицию
            else if (longPositions.Count < MaxPositionsCount)
            {
                // получаем значение индекса, при котором была открыта предыдущая шорт позиция
                decimal indexForPrevPosition;
                try
                {
                    indexForPrevPosition = Convert.ToDecimal(longPositions[longPositions.Count - 1].SignalTypeOpen);
                }
                catch (Exception e)
                {
                    _tabSec.SetNewLogMessage("TryOpenLong: не могу получить значение индекса," +
                                             " при котором была открыта предыдущая лонг позиция", LogMessageType.Error);
                    return;
                }

                // если текущее значение индекса стало больше на заданную величину,
                // чем индекс для предыдущей открытой позиции,
                // то открываем дополнительную лонг позицию
                if (_lastIndex > indexForPrevPosition * (1 + DeviationIndexForAddEnter / 100.0m))
                {
                    OpenLong();
                }
            }
        }

        private void CloseShort(Position shortPosition)
        {
            // если позиция уже закрывается, то ничего не делаем
            if (shortPosition.CloseActiv)
            {
                return;
            }

            // выставляем лимитную заявку на закрытие позиции шорт (покупаем)
            _tabSec.CloseAtLimit(shortPosition,
                _tabSec.PriceBestAsk + Slippage * _tabSec.Securiti.PriceStep,
                shortPosition.OpenVolume);
        }

        private void CloseLong(Position longPosition)
        {
            // если позиция уже закрывается, то ничего не делаем
            if (longPosition.CloseActiv)
            {
                return;
            }

            // выставляем лимитную заявку на закрытие позиции лонг (продаем)
            _tabSec.CloseAtLimit(longPosition,
                _tabSec.PriceBestBid - Slippage * _tabSec.Securiti.PriceStep,
                longPosition.OpenVolume);
        }

        private void CheckClosingPositions(MarketFaze currentMarketFaze)
        {
            // получаем все открытые и открывающиеся позиции 
            var positions = _tabSec.PositionsOpenAll;

            // для каждой позиции проверяем условия её закрытия
            for (int i = 0; positions != null && i < positions.Count; i++)
            {
                // если позиция не имеет статус Open, то ничего с ней не делаем
                if (positions[i].State != PositionStateType.Open)
                {
                    continue;
                }

                // если позиция лонг
                if (positions[i].Direction == Side.Buy)
                {
                    if (MethodOfExit == MethodOfExit.BoundaryChannel &&
                        currentMarketFaze == MarketFaze.Lower)
                    {
                        // закрываем позицию по лимиту
                        CloseLong(positions[i]);
                    }
                    else if (MethodOfExit == MethodOfExit.CenterChannel &&
                            currentMarketFaze == MarketFaze.Low)
                    {
                        // закрываем позицию по лимиту
                        CloseLong(positions[i]);
                    }
                }
                // если позиция шорт
                else if (positions[i].Direction == Side.Sell)
                {
                    if (MethodOfExit == MethodOfExit.BoundaryChannel &&
                        currentMarketFaze == MarketFaze.Upper)
                    {
                        // закрываем позицию по лимиту
                        CloseShort(positions[i]);
                    }
                    else if (MethodOfExit == MethodOfExit.CenterChannel &&
                             currentMarketFaze == MarketFaze.Up)
                    {
                        // закрываем позицию по лимиту
                        CloseShort(positions[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Получить объем входа в позицию
        /// </summary>
        /// <param name="price"></param>
        /// <returns></returns>
        private decimal GetVolume(decimal price)
        {
            // если включен режим фиксированного объема входа, то возвращаем настроечный параметр FixedVolume
            if (OnFixedVolume)
            {
                return FixedVolume;
            }

            // размер депозита в usdt
            decimal usdtValue = 0.0m;

            // если робот запущен в терминале, то получаем размер депозита с биржи
            if (_startProgram.ToString() == "IsOsTrader")
            {
                usdtValue = _tabSec.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == "USDT").ValueCurrent;
            }
            // иначе робот запущен в тестере или оптимизаторе,
            // берем размер депозита из tab.Portfolio.ValueCurrent
            else
            {
                usdtValue = _tabSec.Portfolio.ValueCurrent;
            }

            // расчитываем объем входа в позицию и округляем до заданного значения знаков после запятой
            decimal volume = Math.Round(usdtValue / price * VolumePctOfDeposit / 100.0m, VolumeDecimals);

            return volume;
        }

        #endregion

    }

    // фаза рынка
    public enum MarketFaze
    {
        Upper,
        Up,
        Low,
        Lower,
        Nothing
    }

    // индикатор для построения канала спреда
    public enum ChannelIndicator
    {
        MA_ATR,
        Bollinger
    }

    public enum MethodOfExit
    {
        BoundaryChannel,
        CenterChannel
    }
}
