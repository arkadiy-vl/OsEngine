using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.Charts.CandleChart.Indicators;


namespace OsEngine.Robots.OnScriptIndicators
{
    [Bot("ArbitrageOneLeg")]
    public class ArbitrageOneLeg : BotPanel
    {
        //Вкладки бота
        private BotTabIndex tabIndex;
        private BotTabSimple tabTrade;

        //Индикаторы бота
        private MovingAverage ma;
        private Bollinger bollinger;
        private Atr atr;
        private IvashovRange ivrange;

        //Настраиваемые параметры индикаторов
        public StrategyParameterInt LenghtMA;
        public StrategyParameterInt LenghtBollinger;
        public StrategyParameterDecimal DeviationBollinger;
        public StrategyParameterInt LenghtATR;
        public StrategyParameterDecimal KoefATR;
        public StrategyParameterInt LenghtIvrange;
        public StrategyParameterInt LenghtMAIvrange;


        //Настраиваемые параметры бота
        //режим работы бота
        public StrategyParameterString Regime;
        //объем входа в позицию в процентах
        public StrategyParameterInt VolumePercent;
        //проскальзывание в шагах цены
        public StrategyParameterInt Slippage;
        //число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;
        //индикатор, используемый для построения канала тренда
        public StrategyParameterString ChannelIndicator;

        //Последние значения цен инструментов и индикаторов
        private decimal lastIndex;
        private decimal lastPrice;
        private decimal lastMA;
        private decimal lastBollingerUp;
        private decimal lastBollingerDown;
        private decimal lastATR;
        private decimal lastIvrange;

        decimal lastChannelUp;
        decimal lastChannelDown;

        //Имя программы, которая запустила бота
        private StartProgram startProgram;
        
        public ArbitrageOneLeg(string name, StartProgram _startProgram) : base(name, _startProgram)
        {
            //Запоминаем имя программы, которая запустила бота
            //Это может быть тестер, оптимизатор, терминал
            startProgram = _startProgram;

            //Создаем вкладки бота
            TabCreate(BotTabType.Index);
            tabIndex = TabsIndex[0];

            TabCreate(BotTabType.Simple);
            tabTrade = TabsSimple[0];

            //Задаем настроечные параметры бота
            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            VolumePercent = CreateParameter("Volume (%)", 50, 30, 100, 10);
            Slippage = CreateParameter("Slipage (in price step)", 0, 0, 20, 1);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            ChannelIndicator = CreateParameter("Channel Indicator", "Bollinger", new[] { "Bollinger", "Bollinger+IvashovRange", "MA+ATR", "MA+IvashovRange"});

            //Задаем настроечные параметры индикаторов бота
            LenghtMA = CreateParameter("Lenght MA", 20, 20, 100, 10);
            LenghtBollinger = CreateParameter("Lenght Bollinger", 40, 40, 200, 20);
            DeviationBollinger = CreateParameter("Deviation Bollinger", 1, 0.5m, 2.0m, 0.5m);
            LenghtATR = CreateParameter("Lenght ATR", 20, 20, 100, 10);
            KoefATR = CreateParameter("Koef. ATR", 1, 1, 5, 0.5m);
            LenghtIvrange = CreateParameter("Lenght Ivashov Range", 20, 10, 50, 10);
            LenghtMAIvrange = CreateParameter("Lenght MA Ivashov Range", 20, 10, 50, 10);

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

            //IvashovRange - разновидность стандартного отклонения
            //используется для построения канала спреда
            ivrange = new IvashovRange(name + "IvashovRange", false);
            ivrange = (IvashovRange)tabIndex.CreateCandleIndicator(ivrange, "RangeArea");
            ivrange.Save();

            //Подписываемся на события
            tabIndex.SpreadChangeEvent += TabIndex_SpreadChangeEvent;
            tabTrade.CandleFinishedEvent += Tab_CandleFinishedEvent;
            ParametrsChangeByUser += ArbitrageOneLeg_ParametrsChangeByUser;
        }

        private void ArbitrageOneLeg_ParametrsChangeByUser()
        {
            if (ma.Lenght != LenghtMA.ValueInt)
            {
                ma.Lenght = LenghtMA.ValueInt;
                ma.Reload();
            }

            if (bollinger.Lenght != LenghtBollinger.ValueInt ||
                bollinger.Deviation != DeviationBollinger.ValueDecimal)
            {
                bollinger.Lenght = LenghtBollinger.ValueInt;
                bollinger.Deviation = DeviationBollinger.ValueDecimal;
                bollinger.Reload();
            }

            if (atr.Lenght != LenghtATR.ValueInt)
            {
                atr.Lenght = LenghtATR.ValueInt;
                atr.Reload();
            }

            if (ivrange.LenghtAverage != LenghtIvrange.ValueInt ||
                ivrange.LenghtMa != LenghtMAIvrange.ValueInt)
            {
                ivrange.LenghtAverage = LenghtIvrange.ValueInt;
                ivrange.LenghtMa = LenghtMAIvrange.ValueInt;
                ivrange.Reload();
            }
        }

        private void Tab_CandleFinishedEvent(List<Candle> candlesTab)
        {
            //Проверяем, что вкладка для индекса подключена
            if (tabIndex.IsConnected == false)
            {
                return;
            }

            //Получаем все свечи из вкладки для индекса
            List<Candle> candlesIndex = tabIndex.Candles;

            //Проверяем наличие свечей в индексе и вкладке для торговли
            if (candlesTab == null || candlesTab.Count < 1 ||
                candlesIndex == null || candlesIndex.Count < 1)
            {
                return;
            }

            //Синхронизируем свечи индекса и вкладки для торговли по времени
            if (candlesIndex[candlesIndex.Count - 1].TimeStart == candlesTab[candlesTab.Count - 1].TimeStart)
            {
                TradeLogic(candlesIndex, candlesTab);
            }
        }

        private void TabIndex_SpreadChangeEvent(List<Candle> candlesIndex)
        {
            //Проверяем, что вкладка для торговли подключена
            if (tabTrade.IsConnected == false)
            {
                return;
            }
            
            //Получаем все завершенные свечи из вкладки для торговли
            List<Candle> candlesTab = tabTrade.CandlesFinishedOnly;

            //Проверяем наличие свечей в индексе и вкладке для торговли
            if (candlesTab == null || candlesTab.Count < 1 ||
                candlesIndex == null || candlesIndex.Count < 1)
            {
                return;
            }

            //Проверка на достаточность свечей
            if (candlesIndex.Count < ma.Lenght + 5 ||
                candlesIndex.Count < bollinger.Lenght + 5 ||
                candlesIndex.Count < atr.Lenght + 5 ||
                candlesIndex.Count < ivrange.LenghtMa + 5 ||
                candlesIndex.Count < ivrange.LenghtAverage + 5)
            {
                return;
            }

            //Синхронизируем свечи индекса и вкладки для торговли по времени
            if (candlesIndex[candlesIndex.Count - 1].TimeStart == candlesTab[candlesTab.Count - 1].TimeStart)
            {
                TradeLogic(candlesIndex, candlesTab);
            }
        }

        public override string GetNameStrategyType()
        {
            return "ArbitrageOneLeg";
        }

        public override void ShowIndividualSettingsDialog()
        {
            //не реализовано, параметры бота задаются через настроечные параметры
        }


        // торговая логика
        private void TradeLogic(List<Candle> candlesIndex, List<Candle> candlesTab)
        {
            //Проверка на отключенность бота
            if (Regime.ValueString == "Off")
            {
                return;
            }

            //Проверка на достаточность свечей
            if (candlesIndex.Count < ma.Lenght + 5 ||
                candlesIndex.Count < bollinger.Lenght + 5 ||
                candlesIndex.Count < atr.Lenght + 5 ||
                candlesIndex.Count < ivrange.LenghtMa + 5 ||
                candlesIndex.Count < ivrange.LenghtAverage + 5)
            {
                return;
            }

            //Получаем последние значения инструментов и индикаторов
            lastIndex = candlesIndex[candlesIndex.Count - 1].Close;
            lastPrice = candlesTab[candlesTab.Count - 1].Close;
            lastMA = ma.Values[ma.Values.Count - 1];
            lastBollingerUp = bollinger.ValuesUp[bollinger.ValuesUp.Count - 1];
            lastBollingerDown = bollinger.ValuesDown[bollinger.ValuesDown.Count - 1];
            lastATR = atr.Values[atr.Values.Count - 1];
            lastIvrange = ivrange.Values[ivrange.Values.Count - 1];

            //Проверка на допустимый диапазон значений цен инструментов
            if (lastPrice <= 0 || lastIndex <= 0 ||
                lastPrice > 1000000 || lastIndex > 1000000)
            {
                return;
            }

            //Строим канал индекса
            if (ChannelIndicator.ValueString == "Bollinger")
            {
                lastChannelUp = lastBollingerUp;
                lastChannelDown = lastBollingerDown;
            }
            else if (ChannelIndicator.ValueString == "Bollinger+IvashovRange")
            {
                lastChannelUp = lastBollingerUp + lastIvrange;
                lastChannelDown = lastBollingerUp - lastIvrange;
            }
            else if (ChannelIndicator.ValueString == "MA+ATR")
            {
                lastChannelUp = lastMA + KoefATR.ValueDecimal * lastATR;
                lastChannelDown = lastMA - KoefATR.ValueDecimal * lastATR;
            }
            else if (ChannelIndicator.ValueString == "MA+IvashovRange")
            {
                lastChannelUp = lastMA + lastIvrange;
                lastChannelDown = lastMA - lastIvrange;
            }

            //Получаем все открытые позиции и для каждой открытой позиции проверяем условие выхода
            List<Position> openPositions = tabTrade.PositionsOpenAll;
            if (openPositions != null || openPositions.Count > 0)
            {
                foreach (Position position in openPositions)
                {
                    LogicClosePosition(position);
                }
            }

            //Проверка, что разрешено открывать новые позиции
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            //Если нет открытых позиций, то проверяем на условия на открытие позиции
            //Робот входит только в одну позицию
            if (openPositions == null || openPositions.Count == 0)
            {
                LogicOpenPosition(candlesIndex, candlesTab);
            }
        }


        //Логика открытия позиции
        private void LogicOpenPosition(List<Candle> candlesIndex, List<Candle> candlesTab)
        {
           
            // открытие позиции шорт, находимся выше канала
            if (lastIndex > lastChannelUp &&
                Regime.ValueString != "OnlyLong")
            {
                tabTrade.SellAtLimit(GetVolume(lastPrice),
                    tabTrade.PriceBestBid - Slippage.ValueInt * tabTrade.Securiti.PriceStep);
            }

            // открытие позиции лонг, находимся ниже канала
            else if (lastIndex < lastChannelDown &&
                Regime.ValueString != "OnlyShort")
            {
                tabTrade.BuyAtLimit(GetVolume(lastPrice),
                    tabTrade.PriceBestAsk + Slippage.ValueInt * tabTrade.Securiti.PriceStep);
            }
        }


        // логика закрытия позиции
        private void LogicClosePosition(Position position)
        {
            if (position.State != PositionStateType.Open)
            {
                return;
            }

            // закрытие лонга, находимся выше канала среднеквадратичного отклонения
            if (position.Direction == Side.Buy &&
                lastIndex > lastChannelUp)
            {
                tabTrade.CloseAtLimit(position,
                    tabTrade.PriceBestBid - Slippage.ValueInt * tabTrade.Securiti.PriceStep,
                    position.OpenVolume);

            }
            // закрытие шорта, находимся ниже канала среднеквадратичного отклонения
            else if (position.Direction == Side.Sell &&
                lastIndex < lastChannelDown)
            {
                tabTrade.CloseAtLimit(position,
                    tabTrade.PriceBestAsk + Slippage.ValueInt * tabTrade.Securiti.PriceStep,
                    position.OpenVolume);
            }
        }

        // получить объем входа в позицию по проценту от депозита
        private decimal GetVolume(decimal price)
        {
            decimal usdtValue = 0.0m;

            if (startProgram.ToString() == "IsOsTrader")
            {
                usdtValue = tabTrade.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == "USDT").ValueCurrent;
            }
            else
            {
                usdtValue = tabTrade.Portfolio.ValueCurrent;
            }

            decimal result = Math.Round(usdtValue / price * VolumePercent.ValueInt / 100.0m,
                 VolumeDecimals.ValueInt);

            return result;
        }
    }
}
