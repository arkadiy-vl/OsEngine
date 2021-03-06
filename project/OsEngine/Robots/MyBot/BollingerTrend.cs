﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Indicators;

namespace OsEngine.Robots.MyBot
{

    // Трендовый робот по каналу болинжера с использованием реверсной системы входа/выхода.
    // Проверка условий входа в позицию, выхода из позиции на завершении свечи.
    // Сигнал входа в позицию - пробой канала болинжера.
    // Сигнал выхода из позиции - либо пробой противоположной линии канала болинджера,
    // либо пробой центра канала болинжера.
    // По реверсной системе позицию открываем только после успешного закрытия предыдущей позиции.
    // Если позиция не открылась с первого раза, то пытаемся повторно открыть позицию по лимиту,
    // если позиция снова не открылась, то ничего не делаем.
    // Если позиция не закрылась с первого раза, то пытаемся повторно закрыть позицию по лимиту,
    // если позиция снова не закрылась, то закрываем её по маркету.
    // Опция перевода позиции в безубыток при достижении мин. профита.
    // Размер входа либо фиксированный размер, либо процент от депозита.

    [Bot("BollingerTrend")]
    public class BollingerTrend : BotPanel
    {
        #region // Публичные настроечные параметры робота

        // режим работы
        public StrategyParameterString Regime;

        // режим отладки
        public StrategyParameterBool OnDebug;

        // включить режим фиксированного депозита
        public StrategyParameterBool OnDepositFixed;

        // размер фиксированного депозита
        public StrategyParameterDecimal DepositFixedSize;

        // код имени инструмента, в котором имеем депозит
        public StrategyParameterString DepositNameCode;

        // объём для входа в позицию
        public StrategyParameterInt VolumePercent;

        // длина индикатора Bollinger
        public StrategyParameterInt BollingerPeriod;

        // отклонение индикатора Bollinger
        public StrategyParameterDecimal BollingerDeviation;

        // способ выхода из позиции: по противоположной границе канала, по центру канала
        public StrategyParameterString MethodOutOfPosition;

        // включить выставление стопов для перевода позиции в безубыток
        public StrategyParameterBool OnStopForBreakeven;

        // минимальный профит в процентах для выставления стопа перевода позиции в безубыток
        public StrategyParameterInt MinProfitOnStopBreakeven;

        // проскальзывание в шагах цены
        public StrategyParameterInt Slippage;

        // число знаков после запятой для вычисления объема входа в позицию
        public StrategyParameterInt VolumeDecimals;

        // разность по времени в часах между временем на сервере, где запущен бот, и временем на бирже
        public StrategyParameterInt ShiftTimeExchange;

        #endregion

        #region // Приватные параметры робота

        // вкладки робота
        private BotTabSimple _tab;

        // индикаторы для робота
        private Aindicator _bollinger;
        //private Aindicator adx;

        // последняя цена
        private decimal _lastPrice;

        // максимум и минимум последней свечи
        private decimal _highLastCandle;
        private decimal _lowLastCandle;

        // последний верхний, нижний болинджер и ADX
        private decimal _upBollinger;
        private decimal _downBollinger;

        // последний стакан
        private MarketDepth _lastMarketDepth;

        // максимальная глубина анализа стакана
        private readonly int _maxLevelsInMarketDepth = 10;

        // время актуальности стакана в секундах
        private readonly int _marketDepthRelevanceTime = 5;

        // флаг входа в позицию лонг/шорт по реверсной системе
        private bool _signalInLong = false;
        private bool _signalInShort = false;

        // имя запущщеной программы: тестер (IsTester), робот (IsOsTrade), оптимизатор (IsOsOptimizer)
        private readonly StartProgram _startProgram;

        #endregion

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="name">Имя робота</param>
        /// <param name="startProgram">Программа, в которой запущен робот</param>
        public BollingerTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _startProgram = startProgram;

            // создаем вкладку робота Simple
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // создаем настроечные параметры робота
            Regime = CreateParameter("Режим работы бота", "Off", new[] { "On", "Off", "OnlyClosePosition", "OnlyShort", "OnlyLong" });
            OnDebug = CreateParameter("Включить отладку", false);
            OnDepositFixed = CreateParameter("Включить режим фикс. депозита", false);
            DepositFixedSize = CreateParameter("Размер фикс. депозита", 100, 100.0m, 100, 100);
            DepositNameCode = CreateParameter("Код инструмента, в котором депозит", "USDT", new[] { "USDT", ""});
            VolumePercent = CreateParameter("Объем входа в позицию (%)", 50, 40, 300, 10);
            BollingerPeriod = CreateParameter("Длина болинжера", 100, 50, 200, 10);
            BollingerDeviation = CreateParameter("Отклонение болинжера", 1.5m, 1.0m, 3.0m, 0.2m);
            MethodOutOfPosition = CreateParameter("Метод выхода из позиции", "ChannelBoundary", new[] { "ChannelBoundary", "ChannelCenter" });
            OnStopForBreakeven = CreateParameter("Вкл. стоп для перевода в безубытк", true);
            MinProfitOnStopBreakeven = CreateParameter("Мин. профит для перевода в безубытк (%)", 7, 5, 20, 1);
            Slippage = CreateParameter("Проскальзывание (в шагах цены)", 350, 1, 500, 50);
            VolumeDecimals = CreateParameter("Кол. знаков после запятой для объема", 4, 4, 10, 1);
            ShiftTimeExchange = CreateParameter("Разница времени с биржей", 5, -10, 10, 1);

            // создаем индикаторы на вкладке робота и задаем для них параметры
            _bollinger = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            _bollinger = (Aindicator)_tab.CreateCandleIndicator(_bollinger, "Prime");
            _bollinger.ParametersDigit[0].Value = BollingerPeriod.ValueInt;
            _bollinger.ParametersDigit[1].Value = BollingerDeviation.ValueDecimal;
            _bollinger.Save();

            // подписываемся на события
            _tab.CandleFinishedEvent += Tab_CandleFinishedEvent;
            _tab.PositionClosingFailEvent += Tab_PositionClosingFailEvent;
            _tab.PositionClosingSuccesEvent += Tab_PositionClosingSuccesEvent;
            _tab.PositionOpeningFailEvent += Tab_PositionOpeningFailEvent;
            _tab.MarketDepthUpdateEvent += Tab_MarketDepthUpdateEvent;
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
            if (_bollinger.ParametersDigit[0].Value != BollingerPeriod.ValueInt ||
                _bollinger.ParametersDigit[1].Value != BollingerDeviation.ValueDecimal)
            {
                _bollinger.ParametersDigit[0].Value = BollingerPeriod.ValueInt;
                _bollinger.ParametersDigit[1].Value = BollingerDeviation.ValueDecimal;
                _bollinger.Reload();
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

            // сохраняем длину болинджера
            int bollingerPeriod = (int)_bollinger.ParametersDigit[0].Value;

            // проверка на достаточное количество свечек и наличие данных в болинджере
            if (candles == null || candles.Count < bollingerPeriod + 5 ||
                _bollinger.DataSeries[0].Values == null || _bollinger.DataSeries[1].Values == null)
            {
                return;
            }

            // сохраняем последние значения параметров цены и болинджера для дальнейшего сокращения длины кода
            _lastPrice = candles[candles.Count - 1].Close;
            _highLastCandle = candles[candles.Count - 1].High;
            _lowLastCandle = candles[candles.Count - 1].Low;
            _upBollinger = _bollinger.DataSeries[0].Values[_bollinger.DataSeries[0].Values.Count - 2];
            _downBollinger = _bollinger.DataSeries[1].Values[_bollinger.DataSeries[1].Values.Count - 2];

            // проверка на корректность последних значений цены и болинджера
            if (_lastPrice <= 0 || _upBollinger <= 0 || _downBollinger <= 0)
            {
                _tab.SetNewLogMessage("Tab_CandleFinishedEvent: цена или линии болинждера" +
                        " меньше или равны нулю.", Logging.LogMessageType.Error);
                return;
            }

            // берем все открытые позиции, которые дальше будем проверять на условие закрытия
            List<Position> openPositions = _tab.PositionsOpenAll;

            for (int i = 0; openPositions.Count != 0 && i < openPositions.Count; i++)
            {
                // если позиция не открыта, то ничего не делаем
                if (openPositions[i].State != PositionStateType.Open)
                {
                    continue;
                }

                // выхода из позиции по пробою индикатора Болинжер
                if (MethodOutOfPosition.ValueString == "ChannelCenter")
                {
                    OutOfPositionByCenterChannel(openPositions[i]);
                }
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

            // если включен режим "OnlyClosePosition", то к открытию позиций не переходим
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            // проверка возможности открытия позиции (робот открывает только одну позицию)
            if (openPositions.Count == 0)
            {
                // условие входа в лонг: пробитие ценой верхнего болинджера и с учетом фильтра
                if (_lastPrice > _upBollinger &&
                    candles[candles.Count - 2].Close < _bollinger.DataSeries[0].Values[_bollinger.DataSeries[0].Values.Count - 3] &&
                    Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }

                // условие входа в шорт: пробитие ценой нижнего болинджера и с учетом фильтра
                else if (_lastPrice < _downBollinger &&
                    candles[candles.Count - 2].Close > _bollinger.DataSeries[1].Values[_bollinger.DataSeries[1].Values.Count - 3] &&
                    Regime.ValueString != "OnlyLong")
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
            // если позиция еще не полностью закрылась и у неё остались ордера на закрытие, то закрываем их
            if (position.CloseActiv)
            {
                _tab.CloseAllOrderToPosition(position);
                _tab.SetNewLogMessage($"Tab_PositionClosingFailEvent: у позиции остались активные ордера. Закрываем их.", Logging.LogMessageType.User);
                System.Threading.Thread.Sleep(4000);
            }

            // не закрытую со второго раза позицию закрываем по маркету
            if (position.SignalTypeClose == "reclosing")
            {
                _tab.CloseAtMarket(position, position.OpenVolume);
                _tab.SetNewLogMessage($"Tab_PositionClosingFailEvent: закрытие позиции {position.Number}" +
                                         " по маркету:  объем - {position.OpenVolume}.", Logging.LogMessageType.User);

                return;
            }
            // повторно пытаемся закрыть позицию по лимиту
            else
            {
                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Tab_PositionClosingFailEvent: повторная попытка закрыть позицию {position.Number}" +
                                         " по лимиту.", Logging.LogMessageType.User);

                position.SignalTypeClose = "reclosing";
                if (position.Direction == Side.Buy)
                    CloseLong(position);
                else if (position.Direction == Side.Sell)
                    CloseShort(position);
            }

            return;
        }

        /// <summary>
        /// Обработка события успешного закрытия позиции
        /// </summary>
        /// <param name="position">Успешно закрытая позиция</param>
        private void Tab_PositionClosingSuccesEvent(Position position)
        {
            // после успешного закрытия позиции проверяем, что нет открытых или открывающихся позиций
            // и только после этого открываем противополжную позицию по реверсной системе
            
            if (_tab.PositionsOpenAll.Find(pos => pos.State == PositionStateType.Open ||
            pos.State == PositionStateType.Opening) != null)
            {
                _tab.SetNewLogMessage($"Tab_PositionClosingSuccesEvent: есть открытые или открывающие позиции," +
                                         " поэтому не открываем позицю по реверсной системе", Logging.LogMessageType.Error);
                return;
            }

            // Открытие позиции лонг по реверсной системе
            if(_signalInLong)
            {
                OpenLong();
            }

            // открытие позиции шорт по реверсной системе
            if(_signalInShort)
            {
                OpenShort();
            }
        }

        /// <summary>
        /// Обработка события не удачного открытия позиции
        /// </summary>
        /// <param name="position">Не открытая позиция</param>
        private void Tab_PositionOpeningFailEvent(Position position)
        {
            // если у позиции остались какие-то ордера, то закрываем их
            if (position.OpenActiv)
            {
                _tab.CloseAllOrderToPosition(position);
                _tab.SetNewLogMessage($"Tab_PositionOpeningFailEvent: у позиции остались активные ордера, закрываем их.", Logging.LogMessageType.User);
                System.Threading.Thread.Sleep(4000);
            }

            // не открытую со второго раза позицию не открываем
            if (position.SignalTypeOpen == "reopening")
            {
                _tab.SetNewLogMessage($"Tab_PositionOpeningFailEvent: повторное открытие позиции {position.Number}" +
                                         " не удалось. Прекращаем пытаться открыть позицию.", Logging.LogMessageType.Error);

                _signalInLong = _signalInShort = false;
                return;
            }
            else
            {
                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Tab_PositionOpeningFailEvent: повторная попытка открыть позицию {position.Number}" +
                                         " по лимиту.", Logging.LogMessageType.User);

                position.SignalTypeOpen = "reopening";

                if (position.Direction == Side.Buy)
                    OpenLong(position);
                else if (position.Direction == Side.Sell)
                    OpenShort(position);
            }

            return;
        }

        /// <summary>
        /// Обработка события изменения стакана.
        /// Просто сохраняем в роботе последний полученный стакан.
        /// </summary>
        /// <param name="marketDepth">Полученный стакан</param>
        private void Tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // проверка корректности полученного стакана
            if (marketDepth.Asks.Count != 0 &&
                marketDepth.Bids.Count != 0)
            {
                // просто сохраняем в роботе полученный стакан, чтобы он всегда был актуальный
                _lastMarketDepth = marketDepth;
            }

            return;
        }

        /// <summary>
        /// Метод выхода из позиции по пробою Болинджера и открытия противоположной позиции по реверсной системе
        /// </summary>
        /// <param name="position">Позиция, которая проверяется на условия выхода</param>
        private void OutOfPositionByBollinger(Position position)
        {
            // условие закрытия лонга - пробитие ценой противоположного болинджера
            if (position.Direction == Side.Buy && _lastPrice < _downBollinger)
            {
                CloseLong(position);

                // условие открытия шорта по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyLong")
                {
                    _signalInShort = true;
                }
            }
            // условие закрытия шорта - пробитие ценой противоположного болинджера
            else if (position.Direction == Side.Sell && _lastPrice > _upBollinger)
            {
                CloseShort(position);

                // условие открытия лонга по реверсивной системе
                if (Regime.ValueString != "OnlyClosePosition" &&
                    Regime.ValueString != "OnlyShort")
                {
                    _signalInLong = true;
                }
            }

            return;
        }

        /// <summary>
        /// Метод выхода из позиции по пробою центра канала
        /// </summary>
        /// <param name="position">Позиция, которая проверяется на условия выхода</param>
        private void OutOfPositionByCenterChannel(Position position)
        {
            // последнее значение центра канала
            decimal lastCenterChannel = _downBollinger + (_upBollinger - _downBollinger)/2;

            // условие закрытия лонга - пробитие ценой центра канала
            if (position.Direction == Side.Buy && _lastPrice < lastCenterChannel)
            {
                CloseLong(position);
            }
            // условие закрытия шорта - пробитие ценой центра канала
            else if (position.Direction == Side.Sell && _lastPrice > lastCenterChannel)
            {
                CloseShort(position);
            }

            return;
        }

        /// <summary>
        /// Установка трейлинг стопа (не используется в стратегии!)
        /// </summary>
        /// <param name="position">Позиция для которой устанавливается трейлинг стоп</param>
        private void SetTrailingStop(Position position)
        {
            // размер трейлинг стопа в процентах (при использовании трейлинг стопа в стратегии перенести в настроечные параметры)
            int trailingStopPercent = 7;

            // цена активации стоп ордера
            decimal priceActivation;

            // цена стоп ордера
            decimal priceOrder;

            // установка трейлинг стопа для позиции лонг
            if (position.Direction == Side.Buy)
            {
                // цена активации ставится величину трейлинг стопа от минимума последней свечи
                priceActivation = _lowLastCandle * (1 - trailingStopPercent / 100.0m);

                // цена стоп ордера ставится ниже на величину двух проскальзываний от цены активации
                priceOrder = priceActivation - 2 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                _tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
            }

            // установка трейлинг стопа для позиции шорт
            else if (position.Direction == Side.Sell)
            {
                // цена активации ставится на величину трейлинг стопа от максимума последней свечи
                priceActivation = _highLastCandle * (1.0m + trailingStopPercent / 100.0m);

                // цена стоп ордера ставится выше на величину двух проскальзываний от цены активации
                priceOrder = priceActivation + 2 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                _tab.CloseAtTrailingStop(position, priceActivation, priceOrder);
            }

            return;
        }

        /// <summary>
        /// Метод перевода позиции в безубыток при достижении мин. профита
        /// </summary>
        /// <param name="position">Позиция, которая проверяется на возможность перевода в безубыток</param>
        private void SetStopForBreakeven(Position position)
        {
            if(position.State != PositionStateType.Open)
            {
                return;
            }

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
                    // цена активации устанавливается на 10 проскальзываний выше цены открытия позиции
                    priceActivation = position.EntryPrice + 10 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                    // если цена активации получилась больше или равна последней цене, то ничего не делаем
                    if (priceActivation >= _lastPrice)
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

                    // цена стоп ордера устанавливается ниже на 3 проскальзывания от цены активации
                    priceOrder = priceActivation - 3 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                    _tab.CloseAtStop(position, priceActivation, priceOrder);
                }
                // установка фиксированного стопа для позиции шорт
                else if (position.Direction == Side.Sell)
                {
                    // цена активации устанавливается на 10 проскальзывания ниже цены открытия позиции
                    priceActivation = position.EntryPrice - 10 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                    // если цена активации получилась меньше или равна последней цене, то ничего не делаем
                    if (priceActivation <= _lastPrice)
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

                    // цена стоп ордера устанавливается выше на величину 3 проскальзываний от цены активации
                    priceOrder = priceActivation + 3 * Slippage.ValueInt * _tab.Securiti.PriceStep;

                    _tab.CloseAtStop(position, priceActivation, priceOrder);
                }
            }

            return;
        }

        /// <summary>
        /// Открытие позиции лонг по лимиту
        /// </summary>
        /// <returns>Позиция, которая будет открыта</returns>
        private Position OpenLong(Position position = null)
        {
            // Определяем объем и цену входа в позицию лонг
            decimal volumePosition = GetVolumePosition(_tab.PriceBestAsk, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceBuy(volumePosition);

            // Если объем входа в позицию посчитался не корректно, то не покупаем
            if (volumePosition <= 0)
            {
                _tab.SetNewLogMessage($"OpenLong:  некорректный объем - {volumePosition}. Покупка не возможна.", Logging.LogMessageType.Error);

                // сброс сигнала входа в лонг по реверсной системе
                _signalInLong = false;

                return null;
            }

            // если цена входа в позицию посчиталась не корректно, то не покупаем
            if (pricePosition <= 0)
            {
                _tab.SetNewLogMessage($"OpenLong:  некорректная цена - {pricePosition}. Покупка не возможна.", Logging.LogMessageType.Error);

                // сброс сигнала входа в лонг по реверсной системе
                _signalInLong = false;

                return null;
            }
            
            // покупаем по лимиту
            // к цене входа в позицию добавляем проскальзывание (покупаем дороже)
            decimal pricePositionWithSlippage = pricePosition + Slippage.ValueInt * _tab.Securiti.PriceStep;

            if(position == null)
            {
                // вход в позицию лонг по лимиту
                position = _tab.BuyAtLimit(volumePosition, pricePositionWithSlippage);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Открытие лонга по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {pricePositionWithSlippage}.", Logging.LogMessageType.User);
            }
            else
            {
                // повторный вход в позицию лонг по лимиту
                _tab.BuyAtLimitToPosition(position, pricePositionWithSlippage, volumePosition);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Повторное открытие лонга {position.Number} по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {pricePositionWithSlippage}.", Logging.LogMessageType.User);
            }

            // сброс флага входа в лонг по реверсной системе
            _signalInLong = false;

            return position;
        }

        /// <summary>
        /// Открытие позиции шорт по лимиту
        /// </summary>
        /// <returns>Позиция, которая будет открыта</returns>
        private Position OpenShort(Position position = null)

        {
            // определяем объем и цену входа в позицию шорт
            decimal volumePosition = GetVolumePosition(_tab.PriceBestBid, DepositNameCode.ValueString);
            decimal pricePosition = GetPriceSell(volumePosition);

            // если объем входа в позицию посчитался не корректно, то не продаем
            if (volumePosition <= 0)
            {
                _tab.SetNewLogMessage($"OpenShort:  некорректный объем - {volumePosition}. Продажа не возможна.", Logging.LogMessageType.Error);

                // сброс сигнала входа в шорт по реверсной системе
                _signalInShort = false;

                return null;
            }

            // если цена входа в позицию посчиталась не корректно, то не продаем
            if (pricePosition <= 0)
            {
                _tab.SetNewLogMessage($"OpenShort:  некорректная цена - {pricePosition}. Продажа не возможна.", Logging.LogMessageType.User);

                // сброс сигнала входа в шорт по реверсной системе
                _signalInShort = false;

                return null;
            }

            // продаем по лимиту
            // из цены вход в позицию вычитаем проскальзывание (продаем дешевле)
            decimal pricePositionWithSlippage = pricePosition - Slippage.ValueInt * _tab.Securiti.PriceStep;

            if(position == null)
            {
                // вход в позицию шорт по лимиту
                position = _tab.SellAtLimit(volumePosition, pricePositionWithSlippage);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Открытие шорта по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {pricePositionWithSlippage}.", Logging.LogMessageType.User);
            }
            else
            {
                // повторный вход в позицию шорт по лимиту
                _tab.SellAtLimitToPosition(position, pricePositionWithSlippage, volumePosition);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Повторное открытие шорта {position.Number} по лимиту:  объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {pricePositionWithSlippage}.", Logging.LogMessageType.User);
            }

            // сброс флага входа в шорт по реверсной системе
            _signalInShort = false;

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
                decimal priceClosePosition = pricePosition - Slippage.ValueInt * _tab.Securiti.PriceStep;

                // выход из позиции лонг по лимиту
                _tab.CloseAtLimit(position, priceClosePosition, volumePosition);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Закрытие лонга по лимиту: объем - {volumePosition}, цена - {pricePosition}, цена с проск.- {priceClosePosition}.", Logging.LogMessageType.User);
            }
            // если цена выхода не посчиталась, тогда выход из позиции лонг по маркету
            else
            {
                _tab.CloseAtMarket(position, volumePosition);
                _tab.SetNewLogMessage($"CloseLong: Некорректная цена выхода. Закрытие лонга по маркету: объем - {volumePosition}.", Logging.LogMessageType.Error);
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
                decimal priceClosePosition = pricePosition + Slippage.ValueInt * _tab.Securiti.PriceStep;

                // выход из позиции шорт по лимиту
                _tab.CloseAtLimit(position, priceClosePosition, volumePosition);

                if (OnDebug.ValueBool)
                    _tab.SetNewLogMessage($"Отладка. Закрытие шорта по лимиту: объем - {volumePosition}, цена - {pricePosition}, цена с проск. - {priceClosePosition}.", Logging.LogMessageType.User);
            }
            // если цена выхода из позиции не посчиталась, тогда выход из позиции шорт по маркету
            else
            {
                _tab.CloseAtMarket(position, volumePosition);
                _tab.SetNewLogMessage($"CloseShort: Некорректная цена выхода. Закрытие шорта по маркету: объем - {volumePosition}.", Logging.LogMessageType.Error);
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
                _tab.SetNewLogMessage($"GetVolumePosition: некорректное значение переданной цены.", Logging.LogMessageType.Error);
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
            else if (_startProgram.ToString() == "IsOsTrader" && securityNameCode != "")
            {
                depositValue = _tab.Portfolio.GetPositionOnBoard().Find(pos => pos.SecurityNameCode == securityNameCode).ValueCurrent;
            }
            // иначе робот запущен в тестере/оптимизаторе или депозит на площадке только в одной денежной единице (не имеет кода имени),
            // тогда размер депозита берем стандартным образом
            else
            {
                depositValue = _tab.Portfolio.ValueCurrent;
            }

            // проверка на корректность полученного размера депозита
            if (depositValue <= 0)
            {
                _tab.SetNewLogMessage($"GetVolumePosition: некорректное значение полученного размера депозита.", Logging.LogMessageType.Error);
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
            // если робот запущен не в терминале, то берем последнюю цену
            if (_startProgram.ToString() != "IsOsTrader")
            {
                return _lastPrice;
            }
            // иначе робот запущен в терминале, тогда берем цену из стакана
            else
            {
                // цена покупки
                decimal priceBuy = 0.0m;

                // запас по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из станкана
                int reservLevelAsks = 1;

                // максимально отклонение полученной из стакана цены покупки от лучшего Ask в стакане(в процентах)
                int maxPriceDeviation = 3;

                // проверка на корректность переданного объема
                if (volume <= 0)
                {
                    _tab.SetNewLogMessage($"GetPriceBuy: некорректный объем - {volume}.", Logging.LogMessageType.Error);
                    return 0.0m;
                }

                // проверка на наличие и актуальность стакана
                if (_lastMarketDepth.Asks == null ||
                    _lastMarketDepth.Asks.Count < 5 ||
                    _lastMarketDepth.Time.AddHours(ShiftTimeExchange.ValueInt).AddSeconds(_marketDepthRelevanceTime) < DateTime.Now)
                {
                    _tab.SetNewLogMessage($"GetPriceBuy: некорректный или неактуальный стакан.", Logging.LogMessageType.Error);
                    return _tab.PriceBestAsk;
                }

                // обходим Asks в стакане на глубину анализа стакана
                for (int i = 0;
                    i < _lastMarketDepth.Asks.Count - reservLevelAsks && i < _maxLevelsInMarketDepth;
                    i++)
                {
                    if (_lastMarketDepth.Asks[i].Ask > volume)
                    {
                        priceBuy = _lastMarketDepth.Asks[i + reservLevelAsks].Price;
                        break;
                    }
                }

                if (priceBuy > (1.0m + maxPriceDeviation / 100.0m) * _lastMarketDepth.Asks[0].Price)
                {
                    _tab.SetNewLogMessage($"GetPriceBuy: цена входа в позицию выше допустимого отклонения от лучшего Ask в стакане.", Logging.LogMessageType.Error);
                    return 0.0m;
                }

                return priceBuy;
            }
        }

        /// <summary>
        /// Получение из стакана  цены Bid, по которой можно продать весь объем позиции
        /// </summary>
        /// <param name="volume">Объем позиции, для которого надо определить цену продажи</param>
        /// <returns></returns>
        private decimal GetPriceSell(decimal volume)
        {
            // если робот запущен не в терминале, тогда берем последнюю цену
            if (_startProgram.ToString() != "IsOsTrader")
            {
                return _lastPrice;
            }
            // если робот запущен в терминале, то находим цену продажи из стакана
            else
            {
                // цена продажи
                decimal priceSell = 0.0m;

                // запас по уровням стакана,
                // т.е. насколько уровней выше, чем посчитали, берем цену из стакана
                int reservLevelBids = 1;

                // максимальное отклонение полученной из стакана цены продажи от лучшего Bid в стакане (в процентах)
                int maxPriceDeviation = 3;

                // проверка на корректность переданного объема
                if (volume <= 0)
                {
                    _tab.SetNewLogMessage($"GetPriceSell: некорректный объем {volume}.", Logging.LogMessageType.Error);
                    return 0.0m;
                }

                // проверка на наличие и актуальность стакана
                if (_lastMarketDepth.Bids == null || 
                    _lastMarketDepth.Bids.Count < 5 ||
                    _lastMarketDepth.Time.AddHours(ShiftTimeExchange.ValueInt).AddSeconds(_marketDepthRelevanceTime) < DateTime.Now)
                {
                    _tab.SetNewLogMessage($"GetPriceSell: некорректный или неактуальный стакан.", Logging.LogMessageType.Error);
                    return _tab.PriceBestBid;
                }

                // обходим Bids в стакане на глубину анализа стакана
                for (int i = 0;
                    i < _lastMarketDepth.Bids.Count - reservLevelBids && i < _maxLevelsInMarketDepth;
                    i++)
                {
                    if (_lastMarketDepth.Bids[i].Bid > volume)
                    {
                        priceSell = _lastMarketDepth.Bids[i + reservLevelBids].Price;
                        break;
                    }
                }

                if (priceSell < (1.0m - maxPriceDeviation / 100.0m) * _lastMarketDepth.Bids[0].Price)
                {
                    _tab.SetNewLogMessage($"GetPriceSell: цена входа в позицию ниже допустимого отклонения от лучшего Bid в стакане.", Logging.LogMessageType.Error);
                    return 0.0m;
                }

                return priceSell;
            }
        }

    } //конец класса BollingerTrend

} //конец namespace
