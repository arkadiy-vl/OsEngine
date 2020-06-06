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

        // режим работы
        public StrategyParameterString Regime;

        // режим отладки
        public StrategyParameterBool OnDebug;

        // длина индикатора Bollinger
        public StrategyParameterInt BollingerLength;

        // отклонение индикатора Bollinger
        public StrategyParameterDecimal BollingerDeviation;

        // объём для входа в позицию
        public StrategyParameterInt VolumePercent;

        // проскальзывание в шагах цены
        public StrategyParameterInt Slippage;

        // число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;

        // способ выхода из позиции: реверс, стоп
        public StrategyParameterString MethodOutOfPosition;

        // величина трейлинг стопа
        public StrategyParameterInt TrailingStopPercent;

        // включить выставление стопов для перевода позиции в безубыток
        public StrategyParameterBool OnStopForBreakeven;

        // минимальный профит в процентах для выставления стопа перевода позиции в безубыток
        public StrategyParameterInt MinProfitOnStopBreakeven;

        // код имени инструмента, в котором имеем депозит
        public StrategyParameterString DepositNameCode;

        // последняя цена
        private decimal lastPrice;

        // максимум и минимум последней свечи
        private decimal highLastCandle;
        private decimal lowLastCandle;

        // параметры индикаторов
        private decimal upBollinger;
        private decimal downBollinger;

        // последний стакан
        private MarketDepth lastMarketDepth;

        // флаг обновления стакана
        private bool flagUpdateMarketDepth;

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
            BollingerLength = CreateParameter("Длина болинжера", 100, 50, 200, 10);
            BollingerDeviation = CreateParameter("Отклонение болинжера", 1.5m, 1.0m, 3.0m, 0.2m);
            VolumePercent = CreateParameter("Объем входа в позицию (%)", 50, 40, 300, 10);
            Slippage = CreateParameter("Проскальзывание (в шагах цены)", 350, 1, 500, 50);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            MethodOutOfPosition = CreateParameter("Метод выхода из позиции", "Bollinger-Revers", new[] { "Bollinger-Revers", "Bollinger-TrailingStop" });
            TrailingStopPercent = CreateParameter("Трейлинг стоп (%)", 5, 5, 15, 1);
            OnStopForBreakeven = CreateParameter("Вкл. стоп для перевода в безубытк", true);
            MinProfitOnStopBreakeven = CreateParameter("Мин. профит для перевода в безубытк (%)", 7, 5, 20, 1);
            DepositNameCode = CreateParameter("Код имени инструмента, в котором депозит", "USDT", new[] { "USDT", "" });

            // создаем индикаторы на вкладке робота и задаем для них параметры
            bollinger = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            bollinger = (Aindicator)tab.CreateCandleIndicator(bollinger, "Prime");
            bollinger.ParametersDigit[0].Value = BollingerLength.ValueInt;
            bollinger.ParametersDigit[1].Value = BollingerDeviation.ValueDecimal;
            bollinger.Save();

            // сбрасываем флаг обновления стакана
            flagUpdateMarketDepth = false;

            // подписываемся на события
            tab.CandleFinishedEvent += Tab_CandleFinishedEvent;
            tab.PositionClosingFailEvent += Tab_PositionClosingFailEvent;
            tab.MarketDepthUpdateEvent += Tab_MarketDepthUpdateEvent;
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
        }

        //------------------------------------------------------------
        // Обработка события закрытия свечи - базовая торговая логика
        //------------------------------------------------------------
        private void Tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            int lengthBollinger = (int)bollinger.ParametersDigit[0].Value;

            if (bollinger.DataSeries[0].Values == null ||
                candles == null ||
                candles.Count < lengthBollinger + 4)
            {
                return;
            }

            lastPrice = candles[candles.Count - 1].Close;
            highLastCandle = candles[candles.Count - 1].High;
            lowLastCandle = candles[candles.Count - 1].Low;
            upBollinger = bollinger.DataSeries[0].Values[bollinger.DataSeries[0].Values.Count - 2];
            downBollinger = bollinger.DataSeries[1].Values[bollinger.DataSeries[1].Values.Count - 2];

            if (lastPrice <= 0 || upBollinger <= 0 || downBollinger <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Отладка. Сработало условие - цена или линии болинждера" +
                        " меньше или равны нулю.", Logging.LogMessageType.User);
                return;
            }

            // берем все открытые позиции, которые дальше будем проверять на условие закрытия
            List<Position> openPositions = tab.PositionsOpenAll;

            // проверка позиций на закрытие
            if (openPositions != null && openPositions.Count != 0)
            {
                for (int i = 0; i < openPositions.Count; i++)
                {
                    // если позиция не открыта, то ничего не делаем
                    if (openPositions[i].State != PositionStateType.Open)
                    {
                        continue;
                    }

                    // если позиция уже закрывается, то ничего не делаем
                    if (openPositions[i].State == PositionStateType.Closing ||
                        openPositions[i].CloseActiv == true ||
                        (openPositions[i].CloseOrders != null && openPositions[i].CloseOrders.Count > 0))
                    {
                        continue;
                    }

                    // вариант выхода из позиции по пробою индикатора Болинжер
                    if (MethodOutOfPosition.ValueString == "Bollinger-Revers")
                    {
                        OutFromPositionByBollinger(openPositions[i]);
                    }
                    // вариант выхода из позиции по трейлинг стопу
                    else if (MethodOutOfPosition.ValueString == "Bollinger-TrailingStop")
                    {
                        SetTrailingStop(openPositions[i]);
                    }
                    // вариант вызода из позиции по умолчанию
                    else
                    {
                        OutFromPositionByBollinger(openPositions[i]);
                    }

                    // установка стопа для перевода позиции в безубыток
                    if (OnStopForBreakeven.ValueBool)
                    {
                        SetStopForBreakeven(openPositions[i]);
                    }
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
                // условие входа в лонг 
                if (lastPrice > upBollinger && Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }
                // условие входа в шорт
                else if (lastPrice < downBollinger && Regime.ValueString != "OnlyLong")
                {
                    OpenShort();
                }
            }
        }


        //-----------------------------------------------
        // Обработка события не удачного закрытия позиции
        //-----------------------------------------------
        private void Tab_PositionClosingFailEvent(Position position)
        {
            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Не удалось закрыть позицию {position.Number}.", Logging.LogMessageType.User);
        }

        //------------------------------------
        // Обработка события изменения стакана
        //------------------------------------
        private void Tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            if(marketDepth != null)
            {
                flagUpdateMarketDepth = true;
                lastMarketDepth = marketDepth;
            }
        }
        //----------------------------------------------------------------------------------------------
        // Выход из позиции по пробою Болинджера и открытие противоположной позиции по реверсной системе
        //----------------------------------------------------------------------------------------------
        private void OutFromPositionByBollinger(Position position)
        {
            // условие закрытия шорта
            if (position.Direction == Side.Sell &&
                lastPrice > upBollinger)
            {
                CloseShort(position);
                //position.SignalTypeClose = SignalTypeClose.Bollinger.ToString();

                // условие открытия лонга по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }
            }

            // условие закрытия лонга 
            if (position.Direction == Side.Buy &&
                lastPrice < downBollinger)
            {
                CloseLong(position);
                //position.SignalTypeClose = SignalTypeClose.Bollinger.ToString();

                // условие открытия шорта по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyLong")
                {
                    OpenShort();
                }
            }
            return;
        }

        //--------------------------------
        // Открытие позиции лонг по лимиту
        //--------------------------------
        private Position OpenLong()
        {
            // ждем обновление стакана
            flagUpdateMarketDepth = false;
            while (!flagUpdateMarketDepth)
            {
                continue;
            }

            // Определяем объем и цену входа в позицию лонг
            decimal volumePosition = GetVolumePosition(lastMarketDepth.Asks[1].Price, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceBuy(volumePosition);

            if (volumePosition == 0 || pricePosition == 0)
            {
                return null;
            }

            // к цене входа в позицию добавляем проскальзывание
            decimal priceOpenPosition = pricePosition + Slippage.ValueInt * tab.Securiti.PriceStep;

            // вход в позицию лонг по лимиту
            Position position = tab.BuyAtLimit(volumePosition, priceOpenPosition);

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Открытие лонга по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceOpenPosition}.", Logging.LogMessageType.User);

            return position;
        }

        //--------------------------------
        // Открытие позиции шорт по лимиту
        //--------------------------------
        private Position OpenShort()
        {
            // ждем обновление стакана
            flagUpdateMarketDepth = false;
            while (!flagUpdateMarketDepth)
            {
                continue;
            }
            
            // определяем объем и цену входа в позицию шорт
            decimal volumePosition = GetVolumePosition(lastMarketDepth.Bids[1].Price, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceSell(volumePosition);

            if (volumePosition == 0 || pricePosition == 0)
            {
                return null;
            }
            // к цене входа в позицию добавляем проскальзывание
            decimal priceOpenPosition = pricePosition - Slippage.ValueInt * tab.Securiti.PriceStep;

            // вход в позицию шорт по лимиту
            Position position = tab.SellAtLimit(volumePosition, priceOpenPosition);

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Открытие шорта по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceOpenPosition}.", Logging.LogMessageType.User);

            return position;
        }

        //----------------------
        // Закрытие позиции лонг
        //----------------------
        private void CloseLong(Position position)
        {
            // ждем обновление стакана
            flagUpdateMarketDepth = false;
            while (!flagUpdateMarketDepth)
            {
                continue;
            }

            // получить цену из стакана, по которой можно продать весь объем для закрытия позиции лонг
            decimal volumePosition = position.OpenVolume;
            decimal pricePosition = GetPriceSell(volumePosition);

            // если цена выхода из позиции посчиталась правильно, тогда выход из позиции по лимиту
            if (pricePosition > 0)
            {
                // к цене выхода из позиции добавляем проскальзывание
                decimal priceClosePosition = pricePosition - Slippage.ValueInt * tab.Securiti.PriceStep;

                // выход из позиции лонг по лимиту
                tab.CloseAtLimit(position, priceClosePosition, volumePosition);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Закрытие лонга по лимиту: объем - {volumePosition}, цена - {pricePosition}, цена с проск.- {priceClosePosition}.", Logging.LogMessageType.User);
            }
            // если цена выхода не посчиталась, тогда выход из позиции лонг по маркету
            else
            {
                tab.CloseAtMarket(position, volumePosition);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Закрытие лонга по маркету: объем - {volumePosition}.", Logging.LogMessageType.User);
            }
            return;
        }

        //----------------------
        // Закрытие позиции шорт
        //----------------------
        private void CloseShort(Position position)
        {
            // ждем обновление стакана
            flagUpdateMarketDepth = false;
            while (!flagUpdateMarketDepth)
            {
                continue;
            }

            // получить цену из стакана, по которой можно купить весь объем для закрытия позиции шорт
            decimal volumePosition = position.OpenVolume;
            decimal pricePosition = GetPriceBuy(volumePosition);

            // если цена выхода из позиции посчиталась правильно, тогда выход из позиции шорт по лимиту
            if (pricePosition > 0)
            {
                // к цене выхода из позиции добавляем проскальзывание
                decimal priceClosePosition = pricePosition + Slippage.ValueInt * tab.Securiti.PriceStep;

                // выход из позиции шорт по лимиту
                tab.CloseAtLimit(position, priceClosePosition, volumePosition);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Закрытие шорта по лимиту: объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceClosePosition}.", Logging.LogMessageType.User);
            }
            // если цена выхода из позиции не посчиталась, тогда выход из позиции шорт по маркету
            else
            {
                tab.CloseAtMarket(position, volumePosition);

                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Закрытие шорта по маркету: объем - {volumePosition}.", Logging.LogMessageType.User);
            }
            return;
        }

        //-----------------------------------------------------------------------
        // Установка трейлинг стопа (вариант выхода из позиции по трейлинг стопу)
        //-----------------------------------------------------------------------
        private void SetTrailingStop(Position position)
        {
            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // установка трейлинг стопа для позиции лонг
            if (position.Direction == Side.Buy)
            {
                // цена активации ставится величину трейлинг стопа от максимума последней свечи
                // или на последний нижний болинджер
                priceActivation = Math.Max(lowLastCandle * (1 - TrailingStopPercent.ValueInt/100.0m), bollinger.DataSeries[1].Last);

                // цена стоп ордера ставится ниже на величину двух проскальзываний от цены активации
                priceOrder = priceActivation - 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.TrailingStop.ToString();
            }

            // установка трейлинг стопа для позиции шорт
            else if (position.Direction == Side.Sell)
            {
                // цена активации ставится на величину трейлинг стопа от минимума последней свечи
                // или на последний верхний болинджер
                priceActivation = Math.Min(highLastCandle * (1.0m + TrailingStopPercent.ValueInt/100.0m), bollinger.DataSeries[0].Last);

                // цена стоп ордера ставится выше на величину двух проскальзываний от цены активации
                priceOrder = priceActivation + 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
                position.SignalTypeClose = SignalTypeClose.TrailingStop.ToString();
            }
        }

        //-----------------------------------------------------------------------------
        // Установка стопа для перевода позиции в безубыток при достижении мин. профита
        //-----------------------------------------------------------------------------
        private void SetStopForBreakeven(Position position)
        {
            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // Если профит позиции больше минимально заданного профита
            if (position.ProfitOperationPersent > Convert.ToDecimal(MinProfitOnStopBreakeven.ValueInt))
            {
                // установка фиксированного стопа для позиции лонг
                if (position.Direction == Side.Buy)
                {
                    // вычисление цены активации
                    // цена активации устанавливается на 3 проскальзывания выше цены открытия позиции
                    priceActivation = position.EntryPrice + 3 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    // если цена активации получилась больше или равна последней цене, то ничего не делаем
                    if (priceActivation >= lastPrice)
                    {
                        return;
                    }

                    // если у позиции уже установлена цена активации и она не меньше, чем priceActivation,
                    // то ничего не делаем
                    if (position.StopOrderRedLine > 0 &&
                        priceActivation <= position.StopOrderRedLine)
                    {
                        return;
                    }

                    // цена стоп ордера устанавливается ниже на 2 проскальзывания от цены активации
                    priceOrder = priceActivation - 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    tab.CloseAtStop(position, priceActivation, priceOrder);
                    position.SignalTypeClose = SignalTypeClose.BreakevenStop.ToString();
                }
                // установка фиксированного стопа для позиции шорт
                else if (position.Direction == Side.Sell)
                {
                    // вычисление цены активации
                    // цена активации устанавливается на 3 проскальзывания ниже цены открытия позиции
                    priceActivation = position.EntryPrice - 3 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    // если цена активации получилась меньше или равна последней цене, то ничего не делаем
                    if (priceActivation <= lastPrice)
                    {
                        return;
                    }

                    // если у позиции уже установлена цена активации и она не меньше, чем priceActivation,
                    // то ничего не делаем
                    if (position.StopOrderRedLine > 0 &&
                        priceActivation >= position.StopOrderRedLine)
                    {
                        return;
                    }

                    // цена стоп ордера устанавливается выше на величину двух проскальзываний от цены активации
                    priceOrder = priceActivation + 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                    tab.CloseAtStop(position, priceActivation, priceOrder);
                    position.SignalTypeClose = SignalTypeClose.BreakevenStop.ToString();
                }
            }
        }

        //-----------------------------------------------------------------
        // Получить объем входа в позицию по заданному проценту от депозита
        //-----------------------------------------------------------------
        private decimal GetVolumePosition(decimal price, string securityNameCode)
        {
            // размер депозита
            decimal depositValue = 0.0m;
            
            // объем позиции
            decimal volumePosition = 0.0m;

            // если робот запущен в терминале, то получаем  с биржи размер депозита в инструменте securityNameCode 
            if (startProgram.ToString() == "IsOsTrader" && securityNameCode != "")
            {
                depositValue = tab.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == securityNameCode).ValueCurrent;
            }
            // иначе робот запущен в тестере/оптимизаторе или депозит только в одной денежной единице (не имеет кода имени),
            // тогда размер депозита берем стандартным образом
            else
            {
                depositValue = tab.Portfolio.ValueCurrent;
            }

            // проверка на корректность получения размера депозита и на корректность цены инструмента
            if (depositValue == 0 || price <= 0)
            {
                return 0.0m;
            }

            // вычисляем объем позиции с точность до VolumeDecimals знаков после запятой
            volumePosition = Math.Round(depositValue / price * VolumePercent.ValueInt / 100.0m,
                 VolumeDecimals.ValueInt);

            return volumePosition;
        }

        //--------------------------------------------------------------------
        //Получить цену из стакана, по которой можно купить весь объем позиции
        //--------------------------------------------------------------------
        private decimal GetPriceBuy(decimal volume)
        {
            // если робот запущен в терминале, то получаем стакан и из него находим цену покупки
            if (startProgram.ToString() == "IsOsTrader")
            {
                if (volume == 0)
                    return 0.0m;
                
                // цена покупки
                decimal priceBuy = 0.0m;

                // закладываемый резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из станкана
                int reservLevelAsks = 1;

                // максимально отклонение цены покупки от лучшего предложения (в процентах)
                int maxPriceDeviation = 5;

                for (int i = 0; lastMarketDepth.Asks != null && i < lastMarketDepth.Asks.Count - reservLevelAsks; i++)
                {
                    if (lastMarketDepth.Asks[i].Ask > volume)
                    {
                        priceBuy = lastMarketDepth.Asks[i + reservLevelAsks].Price;
                        break;
                    }
                }
                if (priceBuy > (1.0m + maxPriceDeviation/100.0m) * lastMarketDepth.Asks[0].Price)
                {
                    return 0.0m;
                }

                return priceBuy;
            }
            // иначе робот запущен в тестере или оптимизаторе, тогда берем последнюю цену
            else
            {
                return lastPrice;
            }
        }

        //---------------------------------------------------------------------
        //Получить цену из стакана, по которой можно продать весь объем позиции
        //---------------------------------------------------------------------
        private decimal GetPriceSell(decimal volume)
        {
            if (volume == 0)
                return 0.0m;

            // если робот запущен в терминале, то получаем стакан и из него находим цену продажи
            if (startProgram.ToString() == "IsOsTrader")
            {
                // цена продажи
                decimal priceSell = 0.0m;

                // закладываемый резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из станкана
                int reservLevelAsks = 1;

                // максимально отклонение цены продажи от лучшего спроса (в процентах)
                int maxPriceDeviation = 5;
                

                for (int i = 0; lastMarketDepth.Bids != null && i < lastMarketDepth.Bids.Count - reservLevelAsks; i++)
                {
                    if (lastMarketDepth.Bids[i].Bid > volume)
                    {
                        priceSell = lastMarketDepth.Bids[i + reservLevelAsks].Price;
                        break;
                    }
                }
                if (priceSell < (1.0m - maxPriceDeviation/100.0m) * lastMarketDepth.Bids[0].Price)
                {
                    return 0.0m;
                }

                return priceSell;
            }
            // иначе робот запущен в тестере или оптимизаторе, тогда берем последнюю цену
            else
            {
                return lastPrice;
            }
        }
    }

    // тип сигнала выхода из позиции
    enum SignalTypeClose
    {
        Bollinger,
        TrailingStop,
        BreakevenStop
    }

}
