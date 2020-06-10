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
        #region // Публичные настроечные параметры робота

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

        // разность по времени в часах между временем на сервере, где запущен бот, и временем на бирже
        public int shiftTimeExchange = 5;
        #endregion

        #region // Приватные параметры робота

        // вкладки робота
        private BotTabSimple tab;

        // индикаторы для робота
        private Aindicator bollinger;

        // последняя цена
        private decimal lastPrice;

        // максимум и минимум последней свечи
        private decimal highLastCandle;
        private decimal lowLastCandle;

        // последний верхний и нижний болинджер
        private decimal upBollinger;
        private decimal downBollinger;

        // последний стакан
        private MarketDepth lastMarketDepth;

        // максимальная глубина анализа стакана
        private int MaxLevelsInMarketDepth = 10;

        // время актуальности стакана в секундах
        private int MarketDepthRelevanceTime = 5;

        // имя запущщеной программы: тестер (IsTester), робот (IsOsTrade), оптимизатор (IsOsOptimizer)
        private readonly StartProgram startProgram;
        #endregion

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Программа, в которой запущен робот</param>
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

            // подписываемся на события
            tab.CandleFinishedEvent += Tab_CandleFinishedEvent;
            tab.PositionClosingFailEvent += Tab_PositionClosingFailEvent;
            tab.MarketDepthUpdateEvent += Tab_MarketDepthUpdateEvent;
            ParametrsChangeByUser += BollingerTrend_ParametrsChangeByUser;
        }

        /// <summary>
        /// Сервисный метод получения названия робота
        /// </summary>
        /// <returns>название робота</returns>
        public override string GetNameStrategyType()
        {
            return "BollingerTrend";
        }

        /// <summary>
        /// Сервисный метод вызова окна индивидуальных настроек робота
        /// </summary>
        public override void ShowIndividualSettingsDialog()
        {
            // не требуется, сделано через настроечные параметры
        }

        /// <summary>
        /// Обработка события изменения настроечных параметров робота
        /// </summary>
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

        /// <summary>
        /// Обработка события закрытия свечи - базовая торговая логика
        /// </summary>
        /// <param name="candles">Список свечей</param>
        private void Tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // сохраняем длину болинджера для удобства
            int lengthBollinger = (int)bollinger.ParametersDigit[0].Value;

            // проверка на достаточное количество свечек и наличие данных в болинджере
            if (candles == null || candles.Count < lengthBollinger + 2 ||
                bollinger.DataSeries[0].Values == null || bollinger.DataSeries[1].Values == null)
            {
                return;
            }

            // сохраняем последние значения параметров цены и болинджера для дальнейшего сокращения длины кода
            lastPrice = candles[candles.Count - 1].Close;
            highLastCandle = candles[candles.Count - 1].High;
            lowLastCandle = candles[candles.Count - 1].Low;
            upBollinger = bollinger.DataSeries[0].Values[bollinger.DataSeries[0].Values.Count - 2];
            downBollinger = bollinger.DataSeries[1].Values[bollinger.DataSeries[1].Values.Count - 2];

            // проверка на корректность последних значений цены и болинджера
            if (lastPrice <= 0 || upBollinger <= 0 || downBollinger <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage("Отладка. Сработало условие - цена или линии болинждера" +
                        " меньше или равны нулю.", Logging.LogMessageType.User);
                return;
            }

            // берем все открытые позиции, которые дальше будем проверять на условие закрытия
            List<Position> openPositions = tab.PositionsOpenAll;

            if (openPositions.Count != 0)
            {
                for (int i = 0; i < openPositions.Count; i++)
                {
                    // если позиция не открыта, то ничего не делаем
                    if (openPositions[i].State != PositionStateType.Open)
                    {
                        continue;
                    }

                    // если позиция уже закрывается, то ничего не делаем
                    /*
                    if (openPositions[i].State == PositionStateType.Closing ||
                        openPositions[i].CloseActiv == true ||
                        (openPositions[i].CloseOrders != null && openPositions[i].CloseOrders.Count > 0))
                    {
                        continue;
                    }
                    */

                    // вариант выхода из позиции по пробою индикатора Болинжер
                    if (MethodOutOfPosition.ValueString == "Bollinger-Revers")
                    {
                        OutOfPositionByBollinger(openPositions[i]);
                    }
                    // вариант выхода из позиции по трейлинг стопу
                    else if (MethodOutOfPosition.ValueString == "Bollinger-TrailingStop")
                    {
                        SetTrailingStop(openPositions[i]);
                    }
                    // вариант выхода из позиции по умолчанию
                    else
                    {
                        OutOfPositionByBollinger(openPositions[i]);
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

            // проверка возможности открытия позиции (робот открывает только одну позицию)
            if (openPositions.Count == 0)
            {
                // условие входа в лонг (пробитие ценой верхнего болинджера)
                if (lastPrice > upBollinger && Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }
                // условие входа в шорт (пробитие ценой нижнего болинджера)
                else if (lastPrice < downBollinger && Regime.ValueString != "OnlyLong")
                {
                    OpenShort();
                }
            }

            return;
        }

        /// <summary>
        /// Обработка события не удачного закрытия позиции
        /// </summary>
        /// <param name="position">позиция, которая не закрылась</param>
        private void Tab_PositionClosingFailEvent(Position position)
        {
            // реакция не удачное закрытие позиции задается в настройках сопровождения позиции в самом боте

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Не удалось закрыть позицию {position.Number}.", Logging.LogMessageType.User);

            return;
        }

        /// <summary>
        /// Обработка события изменения стакана
        /// </summary>
        /// <param name="marketDepth">Полученный стакан</param>
        private void Tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
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
                lastMarketDepth = marketDepth;
            }

            return;
        }

        /// <summary>
        /// Метод выхода из позиции по пробою Болинджера и открытия противоположной позиции по реверсной системе
        /// </summary>
        /// <param name="position">Позиция, которая проверяется на условия выхода</param>
        private void OutOfPositionByBollinger(Position position)
        {
            // основное условие закрытия шорта (пробитие ценой противоположного болинджера)
            if (position.Direction == Side.Sell &&
                lastPrice > upBollinger)
            {
                CloseShort(position);

                // условие открытия лонга по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }
            }

            // дополнительное условие закрытие прибыльного шорта при возврате в канал болинджера
            if(position.Direction == Side.Sell &&
                lastPrice > downBollinger &&
                position.ProfitOperationPersent > 3)
            {
                CloseShort(position);
            }

            // основное условие закрытия лонга (пробитие ценой противоположного болинджера)
            if (position.Direction == Side.Buy &&
                lastPrice < downBollinger)
            {
                CloseLong(position);

                // условие открытия шорта по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyLong")
                {
                    OpenShort();
                }
            }

            // дополнительное условие закрытие прибыльного лонга при возврате в канал болинджера
            if (position.Direction == Side.Buy &&
                lastPrice < upBollinger &&
                position.ProfitOperationPersent > 3)
            {
                CloseLong(position);
            }

            return;
        }

        /// <summary>
        /// Метод выхода из позиции по трейлинг стопу (установка трейлинг стопа)
        /// </summary>
        /// <param name="position"></param>
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
                priceActivation = Math.Max(lowLastCandle * (1 - TrailingStopPercent.ValueInt / 100.0m), bollinger.DataSeries[1].Last);

                // цена стоп ордера ставится ниже на величину двух проскальзываний от цены активации
                priceOrder = priceActivation - 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
            }

            // установка трейлинг стопа для позиции шорт
            else if (position.Direction == Side.Sell)
            {
                // цена активации ставится на величину трейлинг стопа от минимума последней свечи
                // или на последний верхний болинджер
                priceActivation = Math.Min(highLastCandle * (1.0m + TrailingStopPercent.ValueInt / 100.0m), bollinger.DataSeries[0].Last);

                // цена стоп ордера ставится выше на величину двух проскальзываний от цены активации
                priceOrder = priceActivation + 2 * Slippage.ValueInt * tab.Securiti.PriceStep;

                tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
            }

            return;
        }

        /// <summary>
        /// Метод перевода позиции в безубыток при достижении мин. профита
        /// </summary>
        /// <param name="position">Позиция, которая проверяется на возможность перевода в безубыток</param>
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
                }
            }

            return;
        }

        /// <summary>
        /// Открытие позиции лонг по лимиту
        /// </summary>
        /// <returns>Позиция, которая будет открыта</returns>
        private Position OpenLong()
        {
            // Определяем объем и цену входа в позицию лонг
            decimal volumePosition = GetVolumePosition(tab.PriceBestAsk, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceBuy(volumePosition);

            // проверка корректности расчитанного объема позиции и цены позиции
            if (volumePosition <= 0 || pricePosition <= 0)
            {
                return null;
            }

            // к цене входа в позицию добавляем проскальзывание (покупаем дороже)
            decimal priceOpenPosition = pricePosition + Slippage.ValueInt * tab.Securiti.PriceStep;

            // вход в позицию лонг по лимиту
            Position position = tab.BuyAtLimit(volumePosition, priceOpenPosition);

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Открытие лонга по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceOpenPosition}.", Logging.LogMessageType.User);

            return position;
        }

        /// <summary>
        /// Открытие позиции шорт по лимиту
        /// </summary>
        /// <returns>Позиция, которая будет открыта</returns>
        private Position OpenShort()
        {
            // определяем объем и цену входа в позицию шорт
            decimal volumePosition = GetVolumePosition(tab.PriceBestBid, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceSell(volumePosition);

            if (volumePosition <= 0 || pricePosition <= 0)
            {
                return null;
            }

            // из цены вход в позицию вычитаем проскальзывание (продаем дешевле)
            decimal priceOpenPosition = pricePosition - Slippage.ValueInt * tab.Securiti.PriceStep;

            // вход в позицию шорт по лимиту
            Position position = tab.SellAtLimit(volumePosition, priceOpenPosition);

            if (OnDebug.ValueBool)
                tab.SetNewLogMessage($"Отладка. Открытие шорта по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceOpenPosition}.", Logging.LogMessageType.User);

            return position;
        }

        /// <summary>
        /// Закрытие позиции лонг (продажа)
        /// </summary>
        /// <param name="position">Позиция, которая будет закрыта</param>
        private void CloseLong(Position position)
        {
            // получить цену из стакана, по которой можно продать весь объем для закрытия позиции лонг
            decimal volumePosition = position.OpenVolume;
            decimal pricePosition = GetPriceSell(volumePosition);

            // если цена выхода из позиции посчиталась правильно, тогда выход из позиции по лимиту
            if (pricePosition > 0)
            {
                // из цены выхода из позиции вычитаем проскальзывание (продаем дешевле)
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

        /// <summary>
        /// Закрытие позиции шорт (покупка)
        /// </summary>
        /// <param name="position"></param>
        private void CloseShort(Position position)
        {
            // получить цену из стакана, по которой можно купить весь объем для закрытия позиции шорт
            decimal volumePosition = position.OpenVolume;
            decimal pricePosition = GetPriceBuy(volumePosition);

            // если цена выхода из позиции посчиталась правильно, тогда выход из позиции шорт по лимиту
            if (pricePosition > 0)
            {
                // к цене выхода из позиции добавляем проскальзывание (покупаем дороже)
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

        /// <summary>
        /// Получение объема входа в позицию по проценту от размера депозита (задается в настроечных параметрах)
        /// </summary>
        /// <param name="price">Цена торгуемого инструмента</param>
        /// <param name="securityNameCode">Код инструмента, в котором имеем депозит на бирже</param>
        /// <returns>Объем для входа в позицию по торгуемому инструменту</returns>
        private decimal GetVolumePosition(decimal price, string securityNameCode = "")
        {
            // проверка на корректность переданной цены инструмента
            if (price <= 0)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Сработало условие в GetVolumePosition: некорректное значение переданной цены.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // размер депозита
            decimal depositValue = 0.0m;
            
            // объем позиции
            decimal volumePosition = 0.0m;

            // если робот запущен в терминале и депозит на площадке может быть в разных денежных единицах,
            // то получаем  с биржи размер депозита в инструменте securityNameCode 
            if (startProgram.ToString() == "IsOsTrader" && securityNameCode != "")
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
                    tab.SetNewLogMessage($"Отладка. Сработало условие в GetVolumePosition: некорректное значение полученного размера депозита.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // вычисляем объем позиции с точность до VolumeDecimals знаков после запятой
            volumePosition = Math.Round(depositValue / price * VolumePercent.ValueInt / 100.0m,
                 VolumeDecimals.ValueInt);

            return volumePosition;
        }

        /// <summary>
        /// Получение из стакана цены Ask, по которой можно купить весь объем позиции
        /// </summary>
        /// <param name="volume">Объем позиции, для которого надо определить цену покупки</param>
        /// <returns>Цена входа в позицию</returns>
        private decimal GetPriceBuy(decimal volume)
        {
            // проверка на корректность переданного объема, на наличие и актуальность стакана
            if (volume <= 0 ||
                lastMarketDepth.Asks == null ||
                lastMarketDepth.Asks.Count < 2 ||
                lastMarketDepth.Time.AddHours(shiftTimeExchange).AddSeconds(MarketDepthRelevanceTime) < DateTime.Now)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Сработало условие в GetPriceBuy: некорректный объем или стакан.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // цена покупки
            decimal priceBuy = 0.0m;

            // если робот запущен в терминале, то находим цену покупки из стакана
            if (startProgram.ToString() == "IsOsTrader")
            {
                // резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из станкана
                int reservLevelAsks = 1;

                // максимально отклонение полученной из стакана цены покупки от лучшего Ask в стакане(в процентах)
                int maxPriceDeviation = 5;

                // обходим Asks в стакане на глубину анализа стакана
                for (int i = 0;
                    i < lastMarketDepth.Asks.Count - reservLevelAsks && i < MaxLevelsInMarketDepth;
                    i++)
                {
                    if (lastMarketDepth.Asks[i].Ask > volume)
                    {
                        priceBuy = lastMarketDepth.Asks[i + reservLevelAsks].Price;
                        break;
                    }
                }
                if (priceBuy > (1.0m + maxPriceDeviation/100.0m) * lastMarketDepth.Asks[0].Price)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. Сработало условие в GetPriceBuy: цена входа в позицию выше допустимого отклонения от лучшего Ask в стакане.", Logging.LogMessageType.User);

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

        /// <summary>
        /// Получение из стакана  цены Bid, по которой можно продать весь объем позиции
        /// </summary>
        /// <param name="volume">Объем позиции, для которого надо определить цену продажи</param>
        /// <returns></returns>
        private decimal GetPriceSell(decimal volume)
        {
            // проверка на корректность переданного объема, на наличие и актуальность стакана
            if (volume <= 0 || lastMarketDepth.Bids == null || lastMarketDepth.Bids.Count < 2 || lastMarketDepth.Time.AddHours(shiftTimeExchange).AddSeconds(MarketDepthRelevanceTime) < DateTime.Now)
            {
                if (OnDebug.ValueBool)
                    tab.SetNewLogMessage($"Отладка. Сработало условие в GetPriceSell: некорректный объем или стакан.", Logging.LogMessageType.User);

                return 0.0m;
            }

            // цена продажи
            decimal priceSell = 0.0m;

            // если робот запущен в терминале, то находим цену продажи из стакана
            if (startProgram.ToString() == "IsOsTrader")
            {
                // резерв по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из стакана
                int reservLevelBids = 1;

                // максимально отклонение полученной из стакана цены продажи от лучшего Bid в стакане(в процентах)
                int maxPriceDeviation = 5;
                
                for (int i = 0; 
                    i < lastMarketDepth.Bids.Count - reservLevelBids && i < MaxLevelsInMarketDepth;
                    i++)
                {
                    if (lastMarketDepth.Bids[i].Bid > volume)
                    {
                        priceSell = lastMarketDepth.Bids[i + reservLevelBids].Price;
                        break;
                    }
                }
                if (priceSell < (1.0m - maxPriceDeviation/100.0m) * lastMarketDepth.Bids[0].Price)
                {
                    if (OnDebug.ValueBool)
                        tab.SetNewLogMessage($"Отладка. Сработало условие в GetPriceSell: цена входа в позицию ниже допустимого отклонения от лучшего Bid в стакане.", Logging.LogMessageType.User);

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
}
