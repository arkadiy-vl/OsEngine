using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Indicators;

namespace OsEngine.Robots.OnScriptIndicators
{
    class BollingerTrend : BotPanel
    {
        // вкладки робота
        private BotTabSimple tab;

        // индикаторы для робота
        private Aindicator bollinger;
        private Aindicator fractalN;

        // режим работы
        public StrategyParameterString Regime;

        // режим отладки
        public StrategyParameterBool OnDebug;

        // длина индикатора Bollinger
        public StrategyParameterInt BollingerLength;

        // отклонение индикатора Bollinger
        public StrategyParameterDecimal BollingerDeviation;

        // длина индикатора FractalN
        public StrategyParameterInt FractalNLength;

        // объём для входа в позицию
        public StrategyParameterInt VolumePercent;

        // проскальзывание в шагах цены
        public StrategyParameterInt Slippage;

        // число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;

        // способ выхода из позиции: реверс, стоп
        public StrategyParameterString MethodOutOfPosition;

        // включить выставление стопов для перевода позиции в безубыток
        public StrategyParameterBool OnStopForBreakeven;

        // минимальный профит в процентах для выставления стопа перевода позиции в безубыток
        public StrategyParameterInt MinProfitOnStopBreakeven;

        // последняя цена
        private decimal lastPrice;
        //MethodOutOfPosition lastMethodOutOfPosition;

        // параметры индикаторов
        private decimal upBollinger;
        private decimal downBollinger;

        // имя запущщеной программы: тестер (IsTester), робот (IsOsTrade), оптимизатор (IsOsOptimizer)
        private readonly StartProgram startProgram;

        // конструктор
        public BollingerTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            this.startProgram = startProgram;

            // создаем вкладку робота Simple
            TabCreate(BotTabType.Simple);
            tab = TabsSimple[0];

            // создаем настроечные параметры робота
            Regime = CreateParameter("Режим работы бота", "Off", new[] { "On", "Off", "OnlyClosePosition", "OnlyShort", "OnlyLong" });
            OnDebug = CreateParameter("Включить отладку", false);
            BollingerLength = CreateParameter("Длина болинжера", 50, 50, 200, 10);
            BollingerDeviation = CreateParameter("Отклонение болинжера", 1.5m, 1.0m, 3.0m, 0.2m);
            FractalNLength = CreateParameter("Длина фрактала", 21, 5, 100, 5);
            VolumePercent = CreateParameter("Объем входа в позицию (%)", 50, 40, 300, 10);
            Slippage = CreateParameter("Проскальзывание (в шагах цены)", 300, 1, 500, 50);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            MethodOutOfPosition = CreateParameter("Метод выхода из позиции", "Bollinger", new[] { "Bollinger", "TrailingStop", "FractalStop" });
            OnStopForBreakeven = CreateParameter("Вкл. стоп для перевода в безубытк", false);
            MinProfitOnStopBreakeven = CreateParameter("Мин. профит для перевода в безубытк (%)", 10, 5, 20, 1);

            // создаем индикаторы на вкладке робота и задаем для них параметры
            bollinger = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            bollinger = (Aindicator)tab.CreateCandleIndicator(bollinger, "Prime");
            bollinger.ParametersDigit[0].Value = BollingerLength.ValueInt;
            bollinger.ParametersDigit[1].Value = BollingerDeviation.ValueDecimal;
            bollinger.Save();

            fractalN = IndicatorsFactory.CreateIndicatorByName("FractalN", name + "FractalN", false);
            fractalN = (Aindicator)tab.CreateCandleIndicator(fractalN, "Prime");
            fractalN.ParametersDigit[0].Value = FractalNLength.ValueInt;
            fractalN.Save();

            // подписываемся на события
            tab.CandleFinishedEvent += Tab_CandleFinishedEvent;
            //tab.PositionClosingFailEvent += Tab_PositionClosingFailEvent;
            ParametrsChangeByUser += BollingerTrend_ParametrsChangeByUser;
        }

        //-----------------
        // сервисная логика
        //-----------------
        public override string GetNameStrategyType()
        {
            return "BollingerTrend";
        }

        public override void ShowIndividualSettingsDialog()
        {
            // не требуется
        }

        // при изменении настроечных параметров индикаторов изменяем параметры индикаторов 
        private void BollingerTrend_ParametrsChangeByUser()
        {
            if (bollinger.ParametersDigit[0].Value != BollingerLength.ValueInt ||
                bollinger.ParametersDigit[1].Value != BollingerDeviation.ValueDecimal)
            {
                bollinger.ParametersDigit[0].Value = BollingerLength.ValueInt;
                bollinger.ParametersDigit[1].Value = BollingerDeviation.ValueDecimal;
                bollinger.Reload();
            }

            if (fractalN.ParametersDigit[0].Value != FractalNLength.ValueInt)
            {
                fractalN.ParametersDigit[0].Value = FractalNLength.ValueInt;
                fractalN.Reload();
            }
        }

        //----------------
        // торговая логика
        //----------------
        private void Tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            int lengthBollinger = (int)bollinger.ParametersDigit[0].Value;
            int lengthFractal = (int)fractalN.ParametersDigit[0].Value;

            if (bollinger.DataSeries[0].Values == null ||
                candles.Count < lengthBollinger + 2 ||
                candles.Count < lengthFractal)
            {
                return;
            }

            lastPrice = candles[candles.Count - 1].Close;
            upBollinger = bollinger.DataSeries[0].Values[bollinger.DataSeries[0].Values.Count - 2];
            downBollinger = bollinger.DataSeries[1].Values[bollinger.DataSeries[1].Values.Count - 2];

            if (lastPrice <= 0 || upBollinger <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Сработало условие - цена или верхняя линии болинждера" +
                        " меньше или равно нулю", Logging.LogMessageType.User);
                return;
            }

            // берем все открытые позиции, которые дальше будем проверять на условие закрытия
            List<Position> openPositions = tab.PositionsOpenAll;

            // проверка позиций на закрытие
            if (openPositions != null && openPositions.Count != 0)
            {
                for (int i = 0; i < openPositions.Count; i++)
                {
                    LogicClosePosition(openPositions[i]);
                }
            }

            // если включен режим "OnlyClosePosition", то к открытию позиций не переходим
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            // проверка возможности открытия позиции
            // робот открывает только одну позицию
            if (openPositions == null || openPositions.Count == 0)
            {
                LogicOpenPosition();
            }

        }


        // логика открытия позиции
        private void LogicOpenPosition()
        {
            OpenPositionAtBollinger();
        }


        // открытие первой позиции по пробою индикатора Болинджер
        // робот открывает только одну позицию
        private void OpenPositionAtBollinger()
        {
            // условие входа в лонг 
            if (lastPrice > upBollinger &&
                Regime.ValueString != "OnlyShort")
            {
                tab.BuyAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                    tab.PriceBestAsk + Slippage.ValueInt * tab.Securiti.PriceStep);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Сработало условие входа в лонг", Logging.LogMessageType.User);
            }
            // условие входа в шорт
            else if (lastPrice < downBollinger &&
                Regime.ValueString != "OnlyLong")
            {
                tab.SellAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                    tab.PriceBestBid - Slippage.ValueInt * tab.Securiti.PriceStep);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Сработало условие входа в шорт", Logging.LogMessageType.User);
            }
        }

        // логика закрытия позиции
        private void LogicClosePosition(Position position)
        {
            // выход по пробою индикатора Болинжер
            // и открытие противоположной позиции по реверсной системе
            if (MethodOutOfPosition.ValueString == "Bollinger")
            {
                OutFromPositionByBollinger(position);
            }
            // выход по трейлинг стопу
            else if (MethodOutOfPosition.ValueString == "TrailingStop")
            {
                SetTrailingStop(position);
            }
            // выход по стопу, устанавливаемому на предыдущий фрактал
            else if (MethodOutOfPosition.ValueString == "FractalStop")
            {
                SetFractalStop(position);
            }
            else
            {
                OutFromPositionByBollinger(position);
            }

            // Установка стопа для перевода позиции в безубыток
            if (OnStopForBreakeven.ValueBool)
            {
                SetStopForBreakeven(position);
            }
        }


        // обработка события не удачного закрытия позиции
        private void Tab_PositionClosingFailEvent(Position position)
        {
            // логика не реализована

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Не удалось закрыть позицию {position.Number}", Logging.LogMessageType.User);
        }


        // выход из позиции по пробою индикатора Болинджер
        // и открытие противоположной позиции по реверсной системе
        private void OutFromPositionByBollinger(Position position)
        {
            // Если позиция уже закрывается, то ничего не делаем
            if (position.State == PositionStateType.Closing || position.CloseActiv == true ||
                (position.CloseOrders != null && position.CloseOrders.Count > 0))
            {
                return;
            }

            // закрытие шорта и открытие лонга по реверсивной системе
            if (position.Direction == Side.Sell &&
                lastPrice > upBollinger)
            {
                tab.CloseAtLimit(position,
                    tab.PriceBestAsk + Slippage.ValueInt * tab.Securiti.PriceStep,
                    position.OpenVolume);
                position.SignalTypeClose = SignalTypeClose.Bollinger.ToString();

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Сработало условие закрытия шорта по болинджеру", Logging.LogMessageType.User);

                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyShort")
                {
                    tab.BuyAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                       tab.PriceBestAsk + Slippage.ValueInt * tab.Securiti.PriceStep);

                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage("Сработало условие открытия лонга по реверсивной системе", Logging.LogMessageType.User);
                }
            }

            // закрытие лонга и открытие шорта по реверсивной системе
            if (position.Direction == Side.Buy &&
                lastPrice < downBollinger)
            {
                tab.CloseAtLimit(position,
                    tab.PriceBestBid - Slippage.ValueInt * tab.Securiti.PriceStep,
                    position.OpenVolume);
                position.SignalTypeClose = SignalTypeClose.Bollinger.ToString();

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Сработало условие закрытия лонга по болинджеру", Logging.LogMessageType.User);

                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyLong")
                {
                    tab.SellAtLimit(GetVolumeFromPercentageOfDeposit(lastPrice),
                        tab.PriceBestBid - Slippage.ValueInt * tab.Securiti.PriceStep);

                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage("Сработало условие открытия шорта по реверсивной системе", Logging.LogMessageType.User);
                }
            }
        }


        // установка трейлинг стопа (вариант выхода из позиции по трейлинг стопу)
        private void SetTrailingStop(Position position)
        {
            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // если позиция закрывается, то ничего не делаем
            if (position.State == PositionStateType.Closing || position.CloseActiv == true ||
               (position.CloseOrders != null && position.CloseOrders.Count > 0))
            {
                return;
            }

            // установка трейлинг стопа для позиции лонг
            if (position.Direction == Side.Buy)
            {
                // цена активации ставится на последний нижний болинджер
                priceActivation = bollinger.DataSeries[1].Last;

                // цена стоп ордера ставится ниже на величину проскальзывания от цены активации
                priceOrder = priceActivation - Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.TrailingStop.ToString();

                //if (OnDebug.ValueBool)
                    //tab.SetNewLogMessage("Выставление трелинг стопа для лонга", Logging.LogMessageType.User);
            }

            // установка трейлинг стопа для позиции шорт
            else if (position.Direction == Side.Sell)
            {
                // цена активации ставится на последний верхний болинджер
                priceActivation = bollinger.DataSeries[0].Last;

                // цена стоп ордера ставится выше на величину проскальзывания от цены активации
                priceOrder = priceActivation + Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.TrailingStop.ToString();

                //if (OnDebug.ValueBool)
                    //tab.SetNewLogMessage("Выставление трелинг стопа для шорта", Logging.LogMessageType.User);
            }
        }


        // Установка стопа на предыдущий фрактал (вариант выхода из позиции по фракталу)
        private void SetFractalStop(Position position)
        {
            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // порядковый номер фрактала, по которому будет ставится стоп
            // нумерация фракталов начинается с конца, последний фрактал - это 1
            int fractalNumber = 1;

            // если позиция закрывается, то ничего не делаем
            if (position.State == PositionStateType.Closing || position.CloseActiv == true ||
               (position.CloseOrders != null && position.CloseOrders.Count > 0))
            {
                return;
            }

            // установка стопа для позиции лонг
            if (position.Direction == Side.Buy)
            {
                // цена активации устанавливается на последний нижний фрактал
                priceActivation = GetLastFractal(fractalN.DataSeries[1], fractalNumber);

                // если последнего нижнежего фрактала нет,
                // тогда цена активации устанавливается на последний нижний болинджер
                if (priceActivation == 0)
                {
                    priceActivation = bollinger.DataSeries[1].Last;
                }

                // цена стоп ордера устанавливается ниже на величину проскальзывания от цены активации
                priceOrder = priceActivation - Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.FractalStop.ToString();
            }

            // установка стопа для позиции шорт
            else if(position.Direction == Side.Sell)
            {
                // цена активации устанавливается на последний верхний фрактал
                priceActivation = GetLastFractal(fractalN.DataSeries[0], fractalNumber);

                // если последнего верхнего фрактала нет,
                // тогда цена активации устанавливается на последний верхний болинджер
                if (priceActivation == 0)
                {
                    priceActivation = bollinger.DataSeries[0].Last;
                }

                // цена стоп ордера устанавливается выше на величину проскальзывания от цены активации
                priceOrder = priceActivation + Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.FractalStop.ToString();
            }
        }


        // Установка стопа для перевода позиции в безубыток при достижении мин. профита
        private void SetStopForBreakeven(Position position)
        {
            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // если позиция закрывается, то ничего не делаем
            if (position.State == PositionStateType.Closing || position.CloseActiv == true ||
               (position.CloseOrders != null && position.CloseOrders.Count > 0))
            {
                return;
            }

            // Проверяем, что профит позиции больше минимально заданного профита
            if (position.ProfitOperationPersent > Convert.ToDecimal(MinProfitOnStopBreakeven.ValueInt))
            {
                // установка стопа для позиции лонг
                if (position.Direction == Side.Buy)
                {

                    // установка цены активации на 2 проскальзывания выше цены открытия позиции
                    priceActivation = position.EntryPrice + 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    // если у позиции уже установлена цена активации и она не меньше, чем priceActivation,
                    // то ничего не делаем
                    if (priceActivation <= position.StopOrderRedLine)
                    {
                        return;
                    }

                    // цена стоп ордера устанавливается ниже на величину проскальзывания от цены активации
                    priceOrder = priceActivation - Slippage.ValueInt * tab.Securiti.PriceStep;

                    tab.CloseAtStop(position, priceActivation, priceOrder);
                    position.SignalTypeClose = SignalTypeClose.BreakevenStop.ToString();
                }

                // установка стопа для позиции лонг
                if (position.Direction == Side.Sell)
                {
                    // установка цены активации на 2 проскальзывания ниже цены открытия позиции
                    priceActivation = position.EntryPrice - 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    // если у позиции уже установлена цена активации и она не меньше, чем priceActivation,
                    // то ничего не делаем
                    if (position.StopOrderRedLine > 0 &&
                        priceActivation >= position.StopOrderRedLine)
                    {
                        return;
                    }

                    // цена стоп ордера устанавливается выше на величину проскальзывания от цены активации
                    priceOrder = priceActivation + Slippage.ValueInt * tab.Securiti.PriceStep;

                    tab.CloseAtStop(position, priceActivation, priceOrder);
                    position.SignalTypeClose = SignalTypeClose.BreakevenStop.ToString();
                }
            }
        }


        // получить значение фрактала из индикатора Fractal
        private decimal GetLastFractal(IndicatorDataSeries series, int fractalNumber)
        {
            decimal fractalValue = 0;

            if (series == null ||  fractalNumber < 1)
            {
                return 0;
            }

            while (fractalNumber > 0)
            {
                for (int i = series.Values.Count - 1; i >= 0; i--)
                {
                    if (series.Values[i] != 0)
                    {
                        fractalValue = series.Values[i];
                        break;
                    }
                }
                fractalNumber--;
            }
            return fractalValue;

        }


        // получить объем входа в позицию по проценту от депозита
        private decimal GetVolumeFromPercentageOfDeposit(decimal price)
        {
            decimal usdtValue = 0.0m;

            if (startProgram.ToString() == "IsOsTrader")
            {
                usdtValue = tab.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == "USDT").ValueCurrent;
            }
            else if (startProgram.ToString() == "IsTester" || startProgram.ToString() == "IsOsOptimizer")
            {
                usdtValue = tab.Portfolio.ValueCurrent;
            }

            decimal result = Math.Round(usdtValue / price * VolumePercent.ValueInt / 100.0m,
                 VolumeDecimals.ValueInt);
            if (OnDebug.ValueBool)
                tab.SetNewLogMessage("Объем позиции:" + result.ToString(), Logging.LogMessageType.User);

            return result;
        }

    }

    enum SignalTypeClose
    {
        Bollinger,
        TrailingStop,
        FractalStop,
        BreakevenStop
    }

}
