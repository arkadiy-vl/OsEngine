using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels;
using OsEngine.Charts.CandleChart.Indicators;

namespace OsEngine.Robots.OnScriptIndicators
{
    public class ArbitrageTwoLeg : BotPanel
    {
        /// <summary>
        /// Публичные настроечные параметры
        /// </summary>
        #region
        // режим работы робота
        public StrategyParameterString Regime;

        // режим отладки
        public StrategyParameterBool OnDebug;

        // включить режим фиксированного депозита
        public StrategyParameterBool OnDepositFixed;

        // размер фиксированного депозита
        public StrategyParameterDecimal DepositFixedSize;

        // код имени инструмента, в котором имеем депозит
        public StrategyParameterString DepositNameCode;

        // объем входа в позицию в процентах для инструмента 2
        //public StrategyParameterInt VolumePercent1;
        public StrategyParameterInt VolumePercent2;

        // коэффициент корреляции между инструментом 1 и инструментом 2
        // по нему определяетмя объем входа в позицию по инструменту 1
        public StrategyParameterDecimal KoefCorelation;

        // проскальзывание в шагах цены
        public StrategyParameterInt Slippage;

        // число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;

        // индикатор, используемый для построения канала тренда
        public StrategyParameterString ChannelIndicator;

        // период индикатора MA, используется для построения канала спреда
        public StrategyParameterInt MAPeriod;

        // период и отклонение индикатора Bollinger, используется для построения канала спреда
        public StrategyParameterInt BollingerPeriod;
        public StrategyParameterDecimal BollingerDeviation;

        // период и коэффициент индикатора ATR, используется для построения канала спреда
        public StrategyParameterInt AtrPeriod;
        public StrategyParameterDecimal AtrKoef;

        // параметры индикатора IvashovRange, используется для построения канала спреда
        public StrategyParameterInt IvrangePeriod;
        public StrategyParameterInt IvrangeMAPeriod;

        // способ выхода из позиции: реверс, стоп
        public StrategyParameterString MethodOutOfPosition;

        // разность по времени в часах между временем на сервере, где запущен бот, и временем на бирже
        public StrategyParameterInt ShiftTimeExchange;

        #endregion

        /// <summary>
        /// Приватные параметры робота
        /// </summary>
        #region
        // вкладки робота
        private BotTabIndex tabIndex;
        private BotTabSimple tabTrade1;
        private BotTabSimple tabTrade2;

        // индикаторы бота
        private MovingAverage ma;
        private Bollinger bollinger;
        private Atr atr;
        private IvashovRange ivrange;

        // последние значения цен инструментов и индикаторов
        private decimal lastIndex;
        private decimal lastPrice1;
        private decimal lastPrice2;
        private decimal lastBollingerUp;
        private decimal lastBollingerDown;
        private decimal lastMA;
        private decimal lastAtr;
        private decimal lastIvrange;

        // последние значения канала спреда
        decimal lastChannelUp;
        decimal lastChannelDown;

        // свечи для индекса и торгуемых инструментов
        private List<Candle> candlesIndex;
        private List<Candle> candlesTabTrade1;
        private List<Candle> candlesTabTrade2;

        // последний стакан для торгуемых инструментов
        private MarketDepth lastMarketDepth1;
        private MarketDepth lastMarketDepth2;

        // максимальная глубина анализа стакана
        private readonly int maxLevelsMarketDepth = 10;

        // время актуальности стакана в секундах
        private readonly int relevanceTimeMarketDepth = 5;

        // флагb входа в позицию, выхода из позиции
        private bool signalIn = false;
        private bool signalOut1 = false;
        private bool signalOut2 = false;

        // имя программы, которая запустила робота
        private StartProgram startProgram;

        #endregion

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Программа, в которой запущен робот</param>
        public ArbitrageTwoLeg(string name, StartProgram _startProgram) : base(name, _startProgram)
        {
            //Запоминаем имя программы, которая запустила робота
            //Это может быть тестер, оптимизатор, терминал
            startProgram = _startProgram;

            //Создаем вкладки робота
            TabCreate(BotTabType.Index);
            tabIndex = TabsIndex[0];
            tabIndex.TabName = "tabIndex";

            TabCreate(BotTabType.Simple);
            tabTrade1 = TabsSimple[0];
            tabTrade1.TabName = "tabTrade1";

            TabCreate(BotTabType.Simple);
            tabTrade2 = TabsSimple[1];
            tabTrade2.TabName = "tabTrade2";

            // создаем настроечные параметры робота
            Regime = CreateParameter("Режим работы бота", "Off", new[] { "On", "Off", "OnlyClosePosition" });
            OnDebug = CreateParameter("Включить отладку", false);
            OnDepositFixed = CreateParameter("Включить режим фикс. депозита", true);
            DepositFixedSize = CreateParameter("Размер фикс. депозита", 50, 10.0m, 100, 10);
            DepositNameCode = CreateParameter("Код инструмента, в котором депозит", "USDT", new[] { "USDT", "" });
            //VolumePercent1 = CreateParameter("Объем входа в позицию для инструмента 1 (%)", 50, 40, 300, 10);
            VolumePercent2 = CreateParameter("Объем входа в позицию для инструмента 2 (%)", 50, 40, 300, 10);
            KoefCorelation = CreateParameter("Коэф. корреляции между инстр.1 и инстр. 2", 1, 1.0m, 1, 1);

            ChannelIndicator = CreateParameter("Channel Indicator", "Bollinger", new[] { "Bollinger", "MA+ATR", "MA+IvashovRange" });
            MAPeriod = CreateParameter("Period MA", 20, 20, 100, 10);
            BollingerPeriod = CreateParameter("Period Bollinger", 40, 40, 200, 20);
            BollingerDeviation = CreateParameter("Deviation Bollinger", 1, 0.5m, 2.0m, 0.5m);
            AtrPeriod = CreateParameter("Period ATR", 20, 20, 100, 10);
            AtrKoef = CreateParameter("Koef. ATR", 1, 1, 5, 0.5m);
            IvrangePeriod = CreateParameter("Period Ivashov Range", 20, 10, 50, 10);
            IvrangeMAPeriod = CreateParameter("Period MA Ivashov Range", 20, 10, 50, 10);

            Slippage = CreateParameter("Проскальзывание (в шагах цены)", 350, 1, 500, 50);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            ShiftTimeExchange = CreateParameter("Разница времени с биржей", 5, -10, 10, 1);

            //Создаем индикаторы
            //Скользящая средняя, используется для центра канала спреда
            ma = new MovingAverage(name + "MA", false);
            ma = (MovingAverage)tabIndex.CreateCandleIndicator(ma, "Prime");
            ma.Save();

            //Болинджер, используется для построения канала спреда
            bollinger = new Bollinger(name + "Bollinger", false);
            bollinger = (Bollinger)tabIndex.CreateCandleIndicator(bollinger, "Prime");
            bollinger.Save();

            //Average True Range, используется для построения канала спреда
            atr = new Atr(name + "ATR", false);
            atr = (Atr)tabIndex.CreateCandleIndicator(atr, "RangeArea");
            atr.Save();

            //IvashovRange - разновидность станд. отклонения, используется для построения канала спреда
            ivrange = new IvashovRange(name + "IvashovRange", false);
            ivrange = (IvashovRange)tabIndex.CreateCandleIndicator(ivrange, "RangeArea");
            ivrange.Save();

            // сбрасываем флаги входа в позицию и выхода из позиции
            signalIn = false;
            signalOut1 = false;
            signalOut2 = false;

            //Подписываемся на события
            tabIndex.SpreadChangeEvent += TabIndex_SpreadChangeEvent;

            tabTrade1.CandleFinishedEvent += TabTrade1_CandleFinishedEvent;
            tabTrade1.MarketDepthUpdateEvent += TabTrade1_MarketDepthUpdateEvent;
            tabTrade1.PositionOpeningSuccesEvent += TabTrade1_PositionOpeningSuccesEvent;
            tabTrade1.PositionOpeningFailEvent += TabTrade1_PositionOpeningFailEvent;
            tabTrade1.PositionClosingSuccesEvent += TabTrade1_PositionClosingSuccesEvent;
            tabTrade1.PositionClosingFailEvent += TabTrade1_PositionClosingFailEvent;

            tabTrade2.CandleFinishedEvent += TabTrade2_CandleFinishedEvent;
            tabTrade2.MarketDepthUpdateEvent += TabTrade2_MarketDepthUpdateEvent;
            tabTrade2.PositionOpeningSuccesEvent += TabTrade2_PositionOpeningSuccesEvent;
            tabTrade2.PositionOpeningFailEvent += TabTrade2_PositionOpeningFailEvent;
            tabTrade2.PositionClosingSuccesEvent += TabTrade2_PositionClosingSuccesEvent;
            tabTrade2.PositionClosingFailEvent += TabTrade2_PositionClosingFailEvent;

            ParametrsChangeByUser += ArbitrageTwoLeg_ParametrsChangeByUser;
        }
       

        /// <summary>
        /// Сервисный метод получения названия робота
        /// </summary>
        /// <returns>название робота</returns>
        public override string GetNameStrategyType()
        {
            return "ArbitrageTwoLeg";
        }

        /// <summary>
        /// Сервисный метод вызова окна индивидуальных настроек робота
        /// </summary>
        public override void ShowIndividualSettingsDialog()
        {
            //не реализовано, параметры робота задаются через настроечные параметры
        }

        /// <summary>
        /// Обработка события изменения настроечных параметров робота
        /// </summary>
        private void ArbitrageTwoLeg_ParametrsChangeByUser()
        {
            if (ma.Lenght != MAPeriod.ValueInt)
            {
                ma.Lenght = MAPeriod.ValueInt;
                ma.Reload();
            }

            if (bollinger.Lenght != BollingerPeriod.ValueInt ||
                bollinger.Deviation != BollingerDeviation.ValueDecimal)
            {
                bollinger.Lenght = BollingerPeriod.ValueInt;
                bollinger.Deviation = BollingerDeviation.ValueDecimal;
                bollinger.Reload();
            }

            if (atr.Lenght != AtrPeriod.ValueInt)
            {
                atr.Lenght = AtrPeriod.ValueInt;
                atr.Reload();
            }

            if (ivrange.LenghtAverage != IvrangePeriod.ValueInt ||
                ivrange.LenghtMa != IvrangeMAPeriod.ValueInt)
            {
                ivrange.LenghtAverage = IvrangePeriod.ValueInt;
                ivrange.LenghtMa = IvrangeMAPeriod.ValueInt;
                ivrange.Reload();
            }
        }

        /// <summary>
        /// Обработка события изменения индекса
        /// </summary>
        /// <param name="candlesIndex">Свечи индекса</param>
        private void TabIndex_SpreadChangeEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            //Проверяем, что вкладки для торговли подключены и количество свечек достаточное
            if (tabTrade1.IsConnected == false ||
                tabTrade2.IsConnected == false ||
                candlesIndex.Count < bollinger.Lenght + 2 ||
                candlesIndex.Count < ma.Lenght + 2 ||
                candlesIndex.Count < atr.Lenght + 2 ||
                candlesIndex.Count < ivrange.LenghtMa + 2 ||
                candlesIndex.Count < ivrange.LenghtAverage + 2)
            {
                return;
            }

            // сохраняем в роботе список свечей индекса, чтобы он всегда был актуален
            // последняя свеча в индексе будет текущей свечой, а не полностью завершенной
            candlesIndex = candles;

            //Проверяем наличие свечей в индексе и вкладках для торговли, а также синхронизируем свечи во вкладках и индексе
            if (candlesTabTrade1 == null || candlesTabTrade1.Count == 0 ||
                candlesTabTrade2 == null || candlesTabTrade2.Count == 0 ||
                candlesIndex == null || candlesIndex.Count == 0 ||
                candlesIndex[candlesIndex.Count - 1].TimeStart != candlesTabTrade1[candlesTabTrade1.Count - 1].TimeStart ||
                candlesIndex[candlesIndex.Count - 1].TimeStart != candlesTabTrade2[candlesTabTrade2.Count - 1].TimeStart)
            {
                return;
            }

            //Проверка на допустимый диапазон значений цен инструментов
            if (candlesTabTrade1[candlesTabTrade1.Count - 1].Close <= 0 ||
                candlesTabTrade2[candlesTabTrade2.Count - 1].Close <= 0)
            {
                return;
            }

            // запускаем торговую логику
            TradeLogic();
            return;
        }

        /// <summary>
        /// Обработка события закрытия свечи для вкладки TabTrade1
        /// </summary>
        /// <param name="candles">Список свечей</param>
        private void TabTrade1_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            //Проверяем, что вкладки для индекса и второго торгуемого инструмента подключены
            if (tabIndex.IsConnected == false ||
                tabTrade2.IsConnected == false)
            {
                return;
            }

            // просто сохраняем в роботе список свечей для вкладки TabTrade1, чтобы он всегда был актуальный
            // это полностью завершенные свечи 
            candlesTabTrade1 = candles;

            return;
        }

        /// <summary>
        /// Обработка события закрытия свечи для вкладки TabTrade2
        /// </summary>
        /// <param name="candles">Список свечей</param>
        private void TabTrade2_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            //Проверяем, что вкладки для индекса и второго торгуемого инструмента подключены
            if (tabIndex.IsConnected == false ||
                tabTrade1.IsConnected == false)
            {
                return;
            }

            // просто сохраняем в роботе список свечей для вкладки TabTrade2, чтобы он всегда был актуальный
            // это полностью завершенные свечи
            candlesTabTrade2 = candles;

            return;
        }

        /// <summary>
        /// Обработка события изменения стакана для вкладки TabTrade1
        /// </summary>
        /// <param name="marketDepth">Полученный стакан</param>
        private void TabTrade1_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // проверка корректности полученного стакана
            if (marketDepth.Asks != null && marketDepth.Asks.Count != 0 &&
                marketDepth.Bids != null && marketDepth.Bids.Count != 0)
            {
                // просто сохраняем в роботе полученный стакан, чтобы он всегда был актуальный
                lastMarketDepth1 = marketDepth;
            }

            return;
        }

        /// <summary>
        /// Обработка события изменения стакана для вкладки TabTrade2
        /// </summary>
        /// <param name="marketDepth">Полученный стакан</param>
        private void TabTrade2_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // проверка корректности полученного стакана
            if (marketDepth.Asks != null && marketDepth.Asks.Count != 0 &&
                marketDepth.Bids != null && marketDepth.Bids.Count != 0)
            {
                // просто сохраняем в роботе полученный стакан, чтобы он всегда был актуальный
                lastMarketDepth2 = marketDepth;
            }

            return;
        }


        /// <summary>
        /// Основная торговая логика
        /// </summary>
        private void TradeLogic()
        {

            // для удобства сохраняем последние значения списков
            lastIndex = candlesIndex[candlesIndex.Count - 1].Close;
            lastPrice1 = candlesTabTrade1[candlesTabTrade1.Count - 1].Close;
            lastPrice2 = candlesTabTrade2[candlesTabTrade2.Count - 1].Close;
            lastBollingerUp = bollinger.ValuesUp[bollinger.ValuesUp.Count - 2];
            lastBollingerDown = bollinger.ValuesDown[bollinger.ValuesDown.Count - 2];
            lastMA = ma.Values[ma.Values.Count - 1];
            lastAtr = atr.Values[atr.Values.Count - 1];
            lastIvrange = ivrange.Values[ivrange.Values.Count - 1];

            // в зависимости от настроек сохраняем получаем канал спреда
            if (ChannelIndicator.ValueString == "Bollinger")
            {
                lastChannelUp = lastBollingerUp;
                lastChannelDown = lastBollingerDown;
            }
            else if (ChannelIndicator.ValueString == "MA+ATR")
            {
                lastChannelUp = lastMA + lastAtr * AtrKoef.ValueDecimal;
                lastChannelDown = lastMA - lastAtr * AtrKoef.ValueDecimal;
            }
            else if (ChannelIndicator.ValueString == "MA+IvashovRange")
            {
                lastChannelUp = lastMA + lastIvrange;
                lastChannelDown = lastMA - lastIvrange;
            }
            else
            {
                lastChannelUp = lastBollingerUp;
                lastChannelDown = lastBollingerDown;
            }

            // получаем все открытые позиции для каждого торгуемого инструмента
            Position position1 = tabTrade1.PositionsOpenAll[0];
            Position position2 = tabTrade2.PositionsOpenAll[0];
            decimal volumePosition1 = 0.0m;
            decimal pricePosition1 = 0.0m;
            decimal pricePositionWithSlippage1 = 0.0m;
            decimal volumePosition2 = 0.0m;
            decimal pricePosition2 = 0.0m;
            decimal pricePositionWithSlippage2 = 0.0m;

            // если нет открытых позиций по обоим инструментам и разрешено открывать новые позиции,
            // то проверяем условия на открытие позиций
            // робот входит только в одну пару позиций, вначале входит в инструмент 2, потом в инструмент 1
            if (position1 == null && position2 == null &&
                Regime.ValueString != "OnlyClosePosition")
            {
                // если пробиваем канал спреда вверх, то продаем спред:
                // открываем лонг по инструменту 2,
                // открываем шорт по инструменту 1 после успешного открытия лонга по инструменту 2
                if (lastIndex > lastChannelUp)
                {
                    // получаем объем позиции по лучшему предложению в стакане
                    volumePosition2 = GetVolumePosition(tabTrade2, tabTrade2.PriceBestAsk);

                    // получаем цену входа в позицию для покупки всего объема позиции
                    pricePosition2 = GetPriceBuy(tabTrade2, volumePosition2);

                    // вычисляем цену входа в позицию с учетом проскальзывания
                    pricePositionWithSlippage2 = pricePosition2 + Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    // выставляем лимитный ордер на покупку
                    tabTrade2.BuyAtLimit(volumePosition2, pricePositionWithSlippage2);

                    // устанавливаем флаг входа в позицию
                    signalIn = true;

                    if (OnDebug.ValueBool)
                        tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. Открытие лонга по лимиту: объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);
                }
                // иначе, если пробиваем канал спреда вниз, то покупаем спред:
                // открываем шорт по инструменту 2,
                // открываем лонг по инструменту 1 после успешного открытия шорта по инструменту 2
                else if (lastIndex < lastChannelDown)
                {
                    volumePosition2 = GetVolumePosition(tabTrade2, tabTrade2.PriceBestBid);
                    pricePosition2 = GetPriceSell(tabTrade2, volumePosition2);
                    pricePositionWithSlippage2 = pricePosition2 - Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    tabTrade2.SellAtLimit(volumePosition2, pricePositionWithSlippage2);
                    signalIn = true;

                    if (OnDebug.ValueBool)
                        tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. Открытие шорта по лимиту: объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);
                }

                return;
            }
            // иначе, если есть открытые позиции по обоим инструментам,
            // то проверяем возможность закрытия данных позиций
            else if (position1 != null && position1.State == PositionStateType.Open &&
                position2 != null && position2.State == PositionStateType.Open)
            {
                // если находимся в покупке спреда и пробиваем канал спреда вверх, то закрываем покупку спреда:
                // закрываем лонг по инструменту 1 (продаем),
                // закрываем шорт по инструменту 2 (покупаем)
                if (position1.Direction == Side.Buy && lastIndex > lastChannelUp)
                {
                    volumePosition1 = position1.OpenVolume;
                    pricePosition1 = GetPriceSell(tabTrade1, volumePosition1);
                    pricePositionWithSlippage1 = pricePosition1 - Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                    // выставляем лимитный ордер на закрытие лонга по инструменту 1
                    tabTrade1.CloseAtLimit(position1, pricePositionWithSlippage1, volumePosition1);
                    signalOut1 = true;

                    volumePosition2 = position2.OpenVolume;
                    pricePosition2 = GetPriceBuy(tabTrade2, volumePosition2);
                    pricePositionWithSlippage2 = pricePosition2 + Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    // выставляем лимитный ордер на закрытие шорта по инструменту 2
                    tabTrade2.CloseAtLimit(position2, pricePositionWithSlippage2, volumePosition2);
                    signalOut2 = true;

                    if (OnDebug.ValueBool)
                    {
                        tabTrade1.SetNewLogMessage($"Отладка. tabTrade1. Закрытие лонга по лимиту: объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);
                        tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. Закрытие шорта по лимиту: объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);
                    }

                }
                // иначе, если находимся в продаже спреда и пробиваем канал спреда вниз, то закрываем продажу спреда:
                // закрываем шорт по инструменту 1 (покупаем),
                // закрываем лонг по инструменту 2 (продаем)
                else if (position1.Direction == Side.Sell && lastIndex < lastChannelDown)
                {
                    volumePosition1 = position1.OpenVolume;
                    pricePosition1 = GetPriceBuy(tabTrade1, volumePosition1);
                    pricePositionWithSlippage1 = pricePosition1 + Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                    // выставляем лимитный ордер на закрытие лонга по инструменту 1
                    tabTrade1.CloseAtLimit(position1, pricePositionWithSlippage1, volumePosition1);
                    signalOut1 = true;

                    volumePosition2 = position2.OpenVolume;
                    pricePosition2 = GetPriceSell(tabTrade2, volumePosition2);
                    pricePositionWithSlippage2 = pricePosition2 - Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    // выставляем лимитный ордер на закрытие шорта по инструменту 2
                    tabTrade2.CloseAtLimit(position2, pricePositionWithSlippage2, volumePosition2);
                    signalOut2 = true;

                    if (OnDebug.ValueBool)
                    {
                        tabTrade1.SetNewLogMessage($"Отладка. tabTrade1. Закрытие шорта по лимиту: объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);
                        tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. Закрытие лонга по лимиту: объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);
                    }
                }

                return;
            }
        }


        /// <summary>
        /// Обработка события удачного открытия позиции по инструменту 1
        /// </summary>
        /// <param name="position1">Открытая позиция</param>
        private void TabTrade1_PositionOpeningSuccesEvent(Position position1)
        {
            signalIn = false;
        }

        /// <summary>
        /// Обработка события неудачного открытия позиции по инструменту 1
        /// </summary>
        /// <param name="position1">Неоткрытая позиция</param>
        private void TabTrade1_PositionOpeningFailEvent(Position position1)
        {
            // если у позиции остались какие-то ордера, то закрываем их
            if (position1.OpenActiv)
            {
                tabTrade1.CloseAllOrderToPosition(position1);

                if (OnDebug.ValueBool)
                    tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Сработало условие в PosOpeningFailEvent: у позиции остались активные ордера. Закрываем их.", Logging.LogMessageType.User);

                System.Threading.Thread.Sleep(2000);
            }

            decimal volumePosition1;
            decimal pricePosition1;
            decimal pricePositionWithSlippage1;

            // если позиция по инструменту 1 не открылась со второго раза, то открываем её по маркету
            if (position1.SignalTypeOpen == "reopening")
            {
                // вычисляем объем позиции, который осталось открыть
                volumePosition1 = position1.MaxVolume - position1.OpenVolume;

                if (position1.Direction == Side.Buy)
                {
                    tabTrade1.BuyAtMarketToPosition(position1, volumePosition1);

                    if (OnDebug.ValueBool)
                        tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Повторное открытие лонга {position1.Number} по маркету.", Logging.LogMessageType.User);
                }
                else if(position1.Direction == Side.Sell)
                {
                    tabTrade1.SellAtMarketToPosition(position1, volumePosition1);

                    if (OnDebug.ValueBool)
                        tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Повторное открытие шорта {position1.Number} по маркету.", Logging.LogMessageType.User);
                }

                return;
            }
            else
            {
                position1.SignalTypeOpen = "reopening";
                volumePosition1 = position1.MaxVolume - position1.OpenVolume;

                if (position1.Direction == Side.Buy)
                {
                    pricePosition1 = GetPriceBuy(tabTrade1, volumePosition1);
                    pricePositionWithSlippage1 = pricePosition1 + Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                    // повторно выставляем лимитный ордер на покупку по лимиту по инструмента 1
                    tabTrade1.BuyAtLimitToPosition(position1, pricePositionWithSlippage1, volumePosition1);

                    if (OnDebug.ValueBool)
                        tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Повторное открытие лонга {position1.Number} по лимиту:  объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);

                }
                else if (position1.Direction == Side.Sell)
                {
                    pricePosition1 = GetPriceSell(tabTrade1, volumePosition1);
                    pricePositionWithSlippage1 = pricePosition1 - Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                    // повторно выставляем лимитный ордер на продажу по лимиту инструмента 1
                    tabTrade1.SellAtLimitToPosition(position1, pricePositionWithSlippage1, volumePosition1);

                    if (OnDebug.ValueBool)
                        tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Повторное открытие шорта {position1.Number} по лимиту:  объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);
                }
            }
            return;
        }

        /// <summary>
        /// Обработка события удачного закрытия позиции по инструменту 1
        /// </summary>
        /// <param name="position1">Закрытая позиция</param>
        private void TabTrade1_PositionClosingSuccesEvent(Position position1)
        {
            signalOut1 = false;
        }

        /// <summary>
        /// Обработка события неудачного закрытия позиции по инструменту 1
        /// </summary>
        /// <param name="position1">Позиция, которую не удалось закрыть</param>
        private void TabTrade1_PositionClosingFailEvent(Position position1)
        {
            // если у позиции остались какие-то ордера, то закрываем их
            if (position1.CloseActiv)
            {
                tabTrade1.CloseAllOrderToPosition(position1);

                if (OnDebug.ValueBool)
                    tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Сработало условие в PosCLosingFailEvent: у позиции остались активные ордера. Закрываем их.", Logging.LogMessageType.User);

                System.Threading.Thread.Sleep(2000);
            }

            // закрываем все позиции по маркету по инструменту 1
            tabTrade1.CloseAllAtMarket();

            if (OnDebug.ValueBool)
                tabTrade1.SetNewLogMessage($"Отладка. TabTrade1. Повторное закрытие всех позиций по маркету.", Logging.LogMessageType.User);

        }

        /// <summary>
        /// Обработка события удачного открытия позиции по инструменту 2
        /// </summary>
        /// <param name="position">Открытая позиция</param>
        private void TabTrade2_PositionOpeningSuccesEvent(Position position2)
        {
            // объем входа в позицию по инструменту 1,
            // определяется по объему открытой позиции по инструменту 2
            decimal volumePosition1 = 0.0m;

            // цена входа в позицию по инструменту 1
            decimal pricePosition1 = 0.0m;

            // цена входа в позицию по инструменту 1 с учетом проскальзывания
            decimal pricePositionWithSlippage1 = 0.0m;

            // открываем позицию по инструменту 1, противоположную позиции по инструменту 2 
            if (position2.Direction == Side.Buy)
            {
                volumePosition1 = KoefCorelation.ValueDecimal * position2.EntryPrice * position2.OpenVolume / tabTrade1.PriceBestBid; ;
                pricePosition1 = GetPriceSell(tabTrade1, volumePosition1);
                pricePositionWithSlippage1 = pricePosition1 - Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                // выставляем лимитный ордер на продажу инструмента 1
                tabTrade1.SellAtLimit(volumePosition1, pricePositionWithSlippage1);

                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. После успеш. открытия лонга, открытие шорта по лимиту по инстр. 1: объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);

                return;
            }
            else if (position2.Direction == Side.Sell)
            {
                volumePosition1 = KoefCorelation.ValueDecimal * position2.EntryPrice * position2.OpenVolume / tabTrade1.PriceBestAsk; ;
                pricePosition1 = GetPriceBuy(tabTrade1, volumePosition1);
                pricePositionWithSlippage1 = pricePosition1 + Slippage.ValueInt * tabTrade1.Securiti.PriceStep;

                // выставляем лимитный ордер на покупку инструмента 1
                tabTrade1.BuyAtLimit(volumePosition1, pricePositionWithSlippage1);

                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. tabTrade2. После успеш. открытия шорта, открытие лонга по лимиту по инстр. 1: объем - {volumePosition1}, цена - {pricePosition1}, цена с проск. - {pricePositionWithSlippage1}.", Logging.LogMessageType.User);

                return;
            }
        }

        /// <summary>
        /// Обработка события неудачного открытия позиции по инструменту 2
        /// </summary>
        /// <param name="position2">Неоткрытая позиция</param>
        private void TabTrade2_PositionOpeningFailEvent(Position position2)
        {
            // если у позиции остались какие-то ордера, то закрываем их
            if (position2.OpenActiv)
            {
                tabTrade2.CloseAllOrderToPosition(position2);

                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Сработало условие в PosOpeningFailEvent: у позиции остались активные ордера. Закрываем их.", Logging.LogMessageType.User);

                System.Threading.Thread.Sleep(2000);
            }

            if (position2.SignalTypeOpen == "reopening")
            {
                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Повторное открытие позиции {position2.Number} по лимиту не удалось. Прекращаем пытаться открыть позицию.", Logging.LogMessageType.User);

                signalIn = false;
                return;
            }
            else
            {
                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Повторная попытка открыть позицию {position2.Number} по лимиту.", Logging.LogMessageType.User);

                position2.SignalTypeOpen = "reopening";
                decimal volumePosition2;
                decimal pricePosition2;
                decimal pricePositionWithSlippage2;

                if (position2.Direction == Side.Buy)
                {
                    volumePosition2 = GetVolumePosition(tabTrade2, tabTrade2.PriceBestAsk, DepositNameCode.ValueString);
                    pricePosition2 = GetPriceBuy(tabTrade2, volumePosition2);
                    pricePositionWithSlippage2 = pricePosition2 + Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    // выставляем лимитный ордер на повторную покупку по лимиту инструмента 2
                    tabTrade2.BuyAtLimitToPosition(position2, pricePositionWithSlippage2, volumePosition2);

                    if (OnDebug.ValueBool)
                        tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Повторное открытие лонга {position2.Number} по лимиту:  объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);

                }
                else if (position2.Direction == Side.Sell)
                {
                    volumePosition2 = GetVolumePosition(tabTrade2, tabTrade2.PriceBestBid, DepositNameCode.ValueString);
                    pricePosition2 = GetPriceSell(tabTrade2, volumePosition2);
                    pricePositionWithSlippage2 = pricePosition2 - Slippage.ValueInt * tabTrade2.Securiti.PriceStep;

                    // выставляем лимитный ордер на повторную продажу по лимиту инструмента 2
                    tabTrade2.SellAtLimitToPosition(position2, pricePositionWithSlippage2, volumePosition2);

                    if (OnDebug.ValueBool)
                        tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Повторное открытие шорта {position2.Number} по лимиту:  объем - {volumePosition2}, цена - {pricePosition2}, цена с проск. - {pricePositionWithSlippage2}.", Logging.LogMessageType.User);
                }
            }
            return;
        }

        /// <summary>
        /// Обработка события удачного закрытия позиции по инструменту 2
        /// </summary>
        /// <param name="position2">Закрытая позиция</param>
        private void TabTrade2_PositionClosingSuccesEvent(Position position2)
        {
            signalOut2 = false;
        }

        /// <summary>
        /// Обработка события неудачного закрытия позиции по инструменту 2
        /// </summary>
        /// <param name="position2">Позиция, которую не удалось закрыть</param>
        private void TabTrade2_PositionClosingFailEvent(Position position2)
        {
            // если у позиции остались какие-то ордера, то закрываем их
            if (position2.CloseActiv)
            {
                tabTrade2.CloseAllOrderToPosition(position2);

                if (OnDebug.ValueBool)
                    tabTrade2.SetNewLogMessage($"Отладка. TabTrade2. Сработало условие в PosCLosingFailEvent: у позиции остались активные ордера. Закрываем их.", Logging.LogMessageType.User);

                System.Threading.Thread.Sleep(2000);
            }

            // закрываем все позиции по маркету по инструменту 2
            tabTrade2.CloseAllAtMarket();

            if (OnDebug.ValueBool)
                tabTrade1.SetNewLogMessage($"Отладка. TabTrade2. Повторное закрытие всех позиций по маркету.", Logging.LogMessageType.User);
        }

        /// <summary>
        /// Получение объема входа в позицию по проценту от размера депозита (задается в настроечных параметрах)
        /// </summary>
        /// <param name="price">Цена торгуемого инструмента</param>
        /// <param name="securityNameCode">Код инструмента, в котором имеем депозит на бирже</param>
        /// <returns>Объем для входа в позицию по торгуемому инструменту</returns>
        private decimal GetVolumePosition(BotTabSimple tab, decimal price, string securityNameCode = "")
        {
            // проверка на корректность переданных в метод параметров
            if (tab != null && price <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Сработало условие в GetVolumePosition: не существует вкладка или некорректное значение переданной цены.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // размер депозита
            decimal depositValue = 0.0m;

            // объем позиции
            decimal volumePosition = 0.0m;

            // если включен режим фиксированного депозита, то задаем фиксированное значение депозита из настроек
            if (OnDepositFixed.ValueBool)
            {
                depositValue = DepositFixedSize.ValueDecimal;
            }
            // если робот запущен в терминале и задан код денежной единцы для депозита
            // то получаем  с биржи размер депозита в инструменте securityNameCode 
            else if (startProgram.ToString() == "IsOsTrader" && securityNameCode != "")
            {
                depositValue = tab.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == securityNameCode).ValueCurrent;
            }
            // иначе робот запущен в тестере/оптимизаторе или депозит на площадке только в одной денежной единице (не имеет кода имени),
            // тогда размер депозита берем стандартным образом
            else
            {
                depositValue = tab.Portfolio.ValueCurrent;
            }

            // проверка на корректность полученного размера депозита
            if (depositValue <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetVolumePosition: некорректное значение полученного размера депозита.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // в зависимости от вкладки, вызвавшей данный метод, берем процент входа в позицию
            int volumePercent = 0;
            if (tab.TabName == "tabTrade1")
            {
                // объем входа в позицию для инструмента 1 всегда вычисляется по объему открытой позиции по инструменту 2
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetVolumePosition: запрос объема позиции по инструменту 1.", Logging.LogMessageType.User);

                return 0.0m;
            }

            else if (tab.TabName == "tabTrade2")
                volumePercent = VolumePercent2.ValueInt;

            // вычисляем объем позиции с точность до VolumeDecimals знаков после запятой
            volumePosition = Math.Round(depositValue / price * volumePercent / 100.0m,
                 VolumeDecimals.ValueInt);

            return volumePosition;
        }

        /// <summary>
        /// Получение из стакана цены Ask, по которой можно купить весь объем позиции
        /// </summary>
        /// <param name="tab">Вкладка робота, которая вызвала данный метод</param>
        /// <param name="volume">Объем позиции, для которого надо определить цену покупки</param>
        /// <returns>Цена входа в позицию</returns>
        private decimal GetPriceBuy(BotTabSimple tab, decimal volume)
        {
            // цена покупки
            decimal priceBuy = 0.0m;

            // если робот запущен в терминале, то находим цену покупки из стакана
            if (startProgram.ToString() == "IsOsTrader")
            {
                // резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из станкана
                int reservLevelAsks = 1;

                // максимально отклонение полученной из стакана цены покупки от лучшего Ask в стакане(в процентах)
                int maxPriceDeviation = 3;

                // проверка на наличие вклдаки и корректность переданного объема
                if (tab == null || volume <= 0)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceBuy: нет вкладки или некорректный объем.", Logging.LogMessageType.User);

                    return 0.0m;
                }

                // в зависимости от вкладки, вызвавшей метод получаем последний стакан
                MarketDepth marketDepth = null;

                if (tab.TabName == "tabTrade1")
                {
                    marketDepth = lastMarketDepth1;
                }
                else if (tab.TabName == "tabTrade2")
                {
                    marketDepth = lastMarketDepth2;
                }

                // проверка на наличие и актуальность стакана
                if (marketDepth.Asks == null ||
                    marketDepth.Asks.Count < 2 ||
                    marketDepth.Time.AddHours(ShiftTimeExchange.ValueInt).AddSeconds(relevanceTimeMarketDepth) < DateTime.Now)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceBuy: некорректный или неактуальный стакан.", Logging.LogMessageType.User);

                    return 0.0m;
                }

                // обходим Asks в стакане на глубину анализа стакана
                for (int i = 0;
                    i < marketDepth.Asks.Count - reservLevelAsks && i < maxLevelsMarketDepth;
                    i++)
                {
                    if (marketDepth.Asks[i].Ask > volume)
                    {
                        priceBuy = marketDepth.Asks[i + reservLevelAsks].Price;
                        break;
                    }
                }
                if (priceBuy > (1.0m + maxPriceDeviation / 100.0m) * marketDepth.Asks[0].Price)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceBuy: цена входа в позицию выше допустимого отклонения от лучшего Ask в стакане.", Logging.LogMessageType.User);

                    priceBuy = 0.0m;
                }

                return priceBuy;
            }
            // иначе робот запущен в тестере или оптимизаторе, тогда берем последнюю цену
            else
            {
                if (tab.TabName == "tabTrade1")
                {
                    priceBuy = lastPrice1;
                }
                else if (tab.TabName == "tabTrade2")
                {
                    priceBuy = lastPrice2;
                }

                return priceBuy;
            }
        }

        /// <summary>
        /// Получение из стакана  цены Bid, по которой можно продать весь объем позиции
        /// </summary>
        /// <param name="tab">Вкладка робота, запустившая данный метод</param>
        /// <param name="volume">Объем позиции, для которого надо определить цену продажи</param>
        /// <returns></returns>
        private decimal GetPriceSell(BotTabSimple tab, decimal volume)
        {
            // цена продажи
            decimal priceSell = 0.0m;

            // если робот запущен в терминале, то находим цену продажи из стакана
            if (startProgram.ToString() == "IsOsTrader")
            {
                // резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из стакана
                int reservLevelBids = 1;

                // максимально отклонение полученной из стакана цены продажи от лучшего Bid в стакане(в процентах)
                int maxPriceDeviation = 3;

                // проверка на наличие вкладки и корректность переданного объема
                if (tab == null || volume <= 0)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceSell: нет вкладки или некорректный объем.", Logging.LogMessageType.User);

                    return 0.0m;
                }

                // в зависимости от вкладки, вызвавшей метод получаем последний стакан
                MarketDepth marketDepth = null;

                if (tab.TabName == "tabTrade1")
                {
                    marketDepth = lastMarketDepth1;
                }
                else if (tab.TabName == "tabTrade2")
                {
                    marketDepth = lastMarketDepth2;
                }

                // проверка на наличие и актуальность стакана
                if (marketDepth.Bids == null ||
                    marketDepth.Bids.Count < 2 ||
                    marketDepth.Time.AddHours(ShiftTimeExchange.ValueInt).AddSeconds(relevanceTimeMarketDepth) < DateTime.Now)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceSell: некорректный или неактуальный стакан.", Logging.LogMessageType.User);

                    return 0.0m;
                }

                // обходим Bids в стакане на глубину анализа стакана
                for (int i = 0;
                    i < marketDepth.Bids.Count - reservLevelBids && i < maxLevelsMarketDepth;
                    i++)
                {
                    if (marketDepth.Bids[i].Bid > volume)
                    {
                        priceSell = marketDepth.Bids[i + reservLevelBids].Price;
                        break;
                    }
                }
                if (priceSell < (1.0m - maxPriceDeviation / 100.0m) * marketDepth.Bids[0].Price)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. {tab.TabName}. Сработало условие в GetPriceSell: цена входа в позицию ниже допустимого отклонения от лучшего Bid в стакане.", Logging.LogMessageType.User);

                    priceSell = 0.0m;
                }

                return priceSell;
            }
            // иначе робот запущен в тестере или оптимизаторе, тогда берем последнюю цену
            else
            {
                if (tab.TabName == "tabTrade1")
                {
                    priceSell = lastPrice1;
                }
                else if (tab.TabName == "tabTrade2")
                {
                    priceSell = lastPrice2;
                }

                return priceSell;
            }
        }
    }
}
