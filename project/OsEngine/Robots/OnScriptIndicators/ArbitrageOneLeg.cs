using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels;
using OsEngine.Charts.CandleChart.Indicators;

namespace OsEngine.Robots.OnScriptIndicators
{
    public class ArbitrageOneLeg : BotPanel
    {
        private BotTabIndex tabIndex;
        private BotTabSimple tab;

        public StrategyParameterString Regime;
        public StrategyParameterInt LenghtMA;
        public StrategyParameterInt VolumePercent;
        // число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;
        public StrategyParameterInt Slippage;

        private IvashovRange ivashovRange;
        private MovingAverage ma;

        private decimal lastIndex;
        private decimal lastPrice;
        private decimal lastMA;
        private decimal lastRange;

        private StartProgram startProgram;

        public ArbitrageOneLeg(string name, StartProgram _startProgram) : base(name, _startProgram)
        {
            startProgram = _startProgram; 
            TabCreate(BotTabType.Index);
            tabIndex = TabsIndex[0];

            TabCreate(BotTabType.Simple);
            tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            LenghtMA = CreateParameter("Lenght MA", 9, 5, 50, 5);
            VolumePercent = CreateParameter("Volume (%)", 50, 30, 100, 10);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            Slippage = CreateParameter("Slipage (in price step)", 0, 0, 20, 1);

            ivashovRange = new IvashovRange("IvashovRange", false);
            ivashovRange = (IvashovRange)tabIndex.CreateCandleIndicator(ivashovRange, "RangeArea");
            ivashovRange.Save();

            ma = new MovingAverage("ma", false);
            ma = (MovingAverage)tabIndex.CreateCandleIndicator(ma, "Prime");
            ma.Save();

            tabIndex.SpreadChangeEvent += TabIndex_SpreadChangeEvent;
            tab.CandleFinishedEvent += Tab_CandleFinishedEvent;
            ParametrsChangeByUser += ArbitrageOneLeg_ParametrsChangeByUser;
        }

        private void ArbitrageOneLeg_ParametrsChangeByUser()
        {
            if(ma.Lenght != LenghtMA.ValueInt)
            {
                ma.Lenght = LenghtMA.ValueInt;
                ma.Reload();
            }
        }

        private void Tab_CandleFinishedEvent(List<Candle> candlesTab)
        {
            List<Candle> candlesIndex = tabIndex.Candles;

            if(candlesTab == null || candlesTab.Count < 1 ||
                candlesIndex == null || candlesIndex.Count < 1)
            {
                return;
            }

            if (candlesIndex[candlesIndex.Count - 1].TimeStart == candlesTab[candlesTab.Count - 1].TimeStart)
            {
                TradeLogic(candlesIndex, candlesTab);
            }
        }

        private void TabIndex_SpreadChangeEvent(List<Candle> candlesIndex)
        {
            List<Candle> candlesTab = tab.CandlesFinishedOnly;

            if (candlesTab == null || candlesTab.Count < 1 ||
                candlesIndex == null || candlesIndex.Count < 1)
            {
                return;
            }

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

        }


        // торговая логика
        private void TradeLogic(List<Candle> candlesIndex, List<Candle> candlesTab)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if(candlesTab.Count < ma.Lenght + 5 ||
                candlesTab.Count < ivashovRange.LenghtMa + 5 ||
                candlesTab.Count < ivashovRange.LenghtAverage + 5)
            {
                return;
            }

            lastIndex = candlesIndex[candlesIndex.Count - 1].Close;
            lastPrice = candlesTab[candlesTab.Count - 1].Close;
            lastMA = ma.Values[ma.Values.Count - 1];
            lastRange = ivashovRange.Values[ivashovRange.Values.Count - 1];

            if (lastPrice <= 0 || lastIndex <= 0)
            {
                return;
            }

            List<Position> openPositions = tab.PositionsOpenAll;

            if (openPositions != null || openPositions.Count > 0)
            {
                foreach (Position position in openPositions)
                {
                    LogicClosePosition(position);
                }
            }

            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            if (openPositions == null || openPositions.Count == 0)
            {
                LogicOpenPosition();
            }
        }


        // логика открытия позиции
        private void LogicOpenPosition()
        {
            // открытие позиции шорт,
            // находимся выше канала среднеквадратичного отклонения
            if (lastIndex > lastMA + lastRange &&
                Regime.ValueString != "OnlyLong")
            {
                tab.SellAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                    tab.PriceBestBid - Slippage.ValueInt * tab.Securiti.PriceStep);

            }
            // открытие позиции лонг,
            // находимся ниже канала среднеквадратичного отклонения
            else if (lastIndex < lastMA - lastRange &&
                Regime.ValueString != "OnlyShort")
            {
                
                tab.BuyAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                    tab.PriceBestAsk + Slippage.ValueInt * tab.Securiti.PriceStep);
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
                lastIndex > lastMA + lastRange)
            {
                tab.CloseAtLimit(position,
                    tab.PriceBestAsk + Slippage.ValueInt * tab.Securiti.PriceStep,
                    position.OpenVolume);

            }
            // закрытие шорта, находимся ниже канала среднеквадратичного отклонения
            else if (position.Direction == Side.Sell &&
                lastIndex < lastMA - lastRange)
            {
                tab.CloseAtLimit(position,
                    tab.PriceBestBid - Slippage.ValueInt * tab.Securiti.PriceStep,
                    position.OpenVolume);
            }
        }

        // получить объем входа в позицию по проценту от депозита
        private decimal GetVolumeFromPercentageOfDeposit(decimal price)
        {
            decimal usdtValue = 0.0m;

            if (startProgram.ToString() == "IsOsTrader")
            {
                usdtValue = tab.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == "USDT").ValueCurrent;
            }
            else
            {
                usdtValue = tab.Portfolio.ValueCurrent;
            }

            decimal result = Math.Round(usdtValue / price * VolumePercent.ValueInt / 100.0m,
                 VolumeDecimals.ValueInt);

            return result;
        }
    }
}
