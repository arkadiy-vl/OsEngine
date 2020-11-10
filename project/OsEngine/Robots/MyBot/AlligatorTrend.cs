using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;

namespace OsEngine.Robots.MyBot
{
    [Bot("AlligatorTrend")]
    public class AlligatorTrend : BotPanel
    {

        #region // Публичные настроечные параметры робота
        public StrategyParameterString Regime;              // режим работы робота

        public StrategyParameterDecimal Volume;             // объём для входа в позицию
        public StrategyParameterInt MaxPositionCount;       // максимальное количество позиций
        public StrategyParameterInt Slippage;               // проскальзывание в шагах цены

        public StrategyParameterInt AlligatorFastLenght;    // длина быстрого алигатора
        public StrategyParameterInt AlligatorMiddleLenght;  // длина среднего алигатора
        public StrategyParameterInt AlligatorSlowLenght;    // длина медленного алигатора
        #endregion

        #region // Приватные параметры робота
        private BotTabSimple _tab;                          // вкладка робота
        private Aindicator _alligator;                       // индикатор алигатор для робота
        private decimal _lastPrice;                         // последняя цена

        // последний быстрый, средний и медленный алигатор
        private decimal _lastFastAlligator;
        private decimal _lastMiddleAlligator;
        private decimal _lastSlowAlligator;
        
        // имя запущщеной программы: тестер (IsTester), робот (IsOsTrade), оптимизатор (IsOsOptimizer)
        private readonly StartProgram _startProgram;

        #endregion
        public AlligatorTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _startProgram = startProgram;

            // создаем вкладку робота
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // создаем настроечные параметры робота
            Regime = CreateParameter("Режим работы бота", "Off", new[] { "On", "Off", "OnlyClosePosition", "OnlyShort", "OnlyLong" });
            AlligatorFastLenght = CreateParameter("Длина быстрого алигатора", 100, 50, 200, 10);
            AlligatorMiddleLenght = CreateParameter("Длина среднего алигатора", 100, 50, 200, 10);
            AlligatorSlowLenght = CreateParameter("Длина медленного алигатора", 100, 50, 200, 10);
            Volume = CreateParameter("Объем входа в позицию", 1.0m, 1.0m, 100.0m, 1.0m);
            MaxPositionCount = CreateParameter("Максимальное количество позиций", 2, 1, 10, 1);
            Slippage = CreateParameter("Проскальзывание (в шагах цены)", 350, 1, 500, 50);

            // создаем индикаторы на вкладке робота и задаем для них параметры
            
            
            _alligator = IndicatorsFactory.CreateIndicatorByName("Alligator", name + "Alligator", false);
            _alligator = (Aindicator)_tab.CreateCandleIndicator(_alligator, "Prime");
            _alligator.ParametersDigit[0].Value = AlligatorSlowLenght.ValueInt;
            _alligator.ParametersDigit[1].Value = AlligatorFastLenght.ValueInt;
            _alligator.ParametersDigit[2].Value = AlligatorMiddleLenght.ValueInt;
            _alligator.Save();

            // подписываемся на события
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            ParametrsChangeByUser += AlligatorTrend_ParametrsChangeByUser;
        }


        #region === Сервисная логика ===
        public override string GetNameStrategyType()
        {
            return "AlligatorTrend";
        }

        public override void ShowIndividualSettingsDialog()
        {
            // не реализовано
        }

        private void AlligatorTrend_ParametrsChangeByUser()
        {
            if(AlligatorFastLenght.ValueInt > AlligatorMiddleLenght.ValueInt ||
                AlligatorFastLenght.ValueInt > AlligatorSlowLenght.ValueInt)
            {
                _tab.SetNewLogMessage("ParametrsChangeByUser: Недопустимые значения параметров алигатора." + 
                    " Длина быстрого алигатора должна быть меньше длины среднего и медленного алигатора", Logging.LogMessageType.Error);
                return;
            }

            if(AlligatorMiddleLenght.ValueInt > AlligatorSlowLenght.ValueInt)
            {
                _tab.SetNewLogMessage("ParametrsChangeByUser: Недопустимые значения параметров алигатора." +
                    " Длина среднего алигатора должна быть меньше длины медленного алигатора", Logging.LogMessageType.Error);
                return;
            }

            if (_alligator.ParametersDigit[0].Value != AlligatorSlowLenght.ValueInt ||
                _alligator.ParametersDigit[1].Value != AlligatorFastLenght.ValueInt ||
                _alligator.ParametersDigit[2].Value != AlligatorMiddleLenght.ValueInt)
            {
                _alligator.ParametersDigit[0].Value = AlligatorSlowLenght.ValueInt;
                _alligator.ParametersDigit[1].Value = AlligatorFastLenght.ValueInt;
                _alligator.ParametersDigit[2].Value = AlligatorMiddleLenght.ValueInt;
                _alligator.Reload();
            }
        }

        #endregion


        #region === Торговая логика ===
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            // проверяем, что робот включен
            if (Regime.ValueString == "Off")
            {
                return;
            }

            int alligatorSlowLenght = (int)_alligator.ParametersDigit[0].Value;
            int alligatorFastLenght = (int)_alligator.ParametersDigit[1].Value;
            int alligatorMiddleLenght = (int)_alligator.ParametersDigit[2].Value;

            // если длина быстрой линии аллигатора больше длины средней или медленной линии
            // или длины средней линии аллигатора больше длины медленной линии,
            // то выходим (ничего не делаем)
            if (alligatorFastLenght > alligatorMiddleLenght ||
               alligatorFastLenght > alligatorSlowLenght ||
               alligatorMiddleLenght > alligatorSlowLenght)
            {
                return;
            }
            
            // проверка на достаточное количество свечек и наличие данных в алигаторе
            if (candles == null ||
                candles.Count < alligatorSlowLenght + 5 ||
                _alligator.DataSeries[0] == null ||
                _alligator.DataSeries[1] == null ||
                _alligator.DataSeries[2] == null)
            {
                return;
            }

            // сохраняем последние значения параметров цены и алигатора для дальнейшего сокращения длины кода
            _lastPrice = candles[candles.Count - 1].Close;
            _lastSlowAlligator = _alligator.DataSeries[0].Values[_alligator.DataSeries[0].Values.Count - 1];
            _lastFastAlligator = _alligator.DataSeries[1].Values[_alligator.DataSeries[1].Values.Count - 1];
            _lastMiddleAlligator = _alligator.DataSeries[2].Values[_alligator.DataSeries[2].Values.Count - 1];

            // проверка на корректность последних значений цены и болинджера
            if (_lastPrice <= 0 || _lastFastAlligator <= 0 || _lastMiddleAlligator <= 0 || _lastSlowAlligator <= 0)
            {
                _tab.SetNewLogMessage("Tab_CandleFinishedEvent: цена или линии алигатора" +
                        " меньше или равны нулю.", Logging.LogMessageType.Error);
                return;
            }

            // берем все открытые позиции, которые дальше будем проверять на условие закрытия
            List<Position> openPositions = _tab.PositionsOpenAll;

            List<Position> openLongPositions = _tab.PositionOpenLong;
            List<Position> openShortPositions = _tab.PositionOpenShort;

            for (int i = 0; openPositions.Count != 0 && i < openPositions.Count; i++)
            {
                // если позиция не открыта, то ничего не делаем
                if (openPositions[i].State != PositionStateType.Open)
                {
                    continue;
                }

                // условие выхода из лонга
               if (openPositions[i].Direction == Side.Buy &&
                    (_lastFastAlligator <= _lastMiddleAlligator || _lastMiddleAlligator <= _lastSlowAlligator))
                {
                    CloseLong(openPositions[i]);
                }

                // условие выхода из шорта
                if (openPositions[i].Direction == Side.Sell &&
                    (_lastFastAlligator >= _lastMiddleAlligator || _lastMiddleAlligator >= _lastSlowAlligator))
                {
                    CloseShort(openPositions[i]);
                }
            }

            // если включен режим "OnlyClosePosition", то к открытию позиций не переходим
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            // проверка условий открытия позиций, робот открывает не более MaxPositionCount позиций
            if (openPositions.Count < MaxPositionCount.ValueInt)
            {
                decimal pricePrevOpenLongPosition = 0.0m;

                // если есть открытая лонг позиция, то сохраняем её цену
                if(openLongPositions.Count > 0)
                {
                    pricePrevOpenLongPosition = openLongPositions[openLongPositions.Count - 1].EntryPrice;
                }

                // условие входа в лонг: цена выше всех алигаторов и алигатор смотрит вверх
                if (_lastPrice > pricePrevOpenLongPosition * 1.01m &&
                    _lastPrice > _lastFastAlligator &&
                    _lastFastAlligator > _lastMiddleAlligator &&
                    _lastMiddleAlligator > _lastSlowAlligator &&
                    Regime.ValueString != "OnlyShort")
                {
                    OpenLong();
                }

                decimal pricePrevOpenShortPosition = 9999999999.0m;

                // если есть открытая шорт позиция, то сохраняем её цену
                if (openShortPositions.Count > 0)
                {
                    pricePrevOpenShortPosition = openShortPositions[openShortPositions.Count - 1].EntryPrice;
                }

                // условие входа в шорт: цена ниже всех алигаторов и алигатор смотрит вниз
                else if (_lastPrice < pricePrevOpenShortPosition * 0.99m &&
                    _lastPrice < _lastFastAlligator &&
                    _lastFastAlligator < _lastMiddleAlligator &&
                    _lastMiddleAlligator < _lastSlowAlligator &&
                    Regime.ValueString != "OnlyLong")
                {
                    OpenShort();
                }
            }
        }

        /// <summary>
        /// Открытие позиции лонг по лимиту
        /// </summary>
        /// <returns></returns>
        private void OpenLong()
        {
            // покупаем по лимиту  (покупаем дороже)
            decimal pricePosition = _tab.PriceBestAsk + Slippage.ValueInt * _tab.Securiti.PriceStep;
            var position = _tab.BuyAtLimit(Volume.ValueDecimal, pricePosition);
        }

        /// <summary>
        /// Открытие позиции шорт по лимиту
        /// </summary>
        /// <returns></returns>
        private void OpenShort()
        {
            // продаем по лимиту (продаем дешевле)
            decimal pricePosition = _tab.PriceBestBid - Slippage.ValueInt * _tab.Securiti.PriceStep;
            var position = _tab.SellAtLimit(Volume.ValueDecimal, pricePosition);
        }

        /// <summary>
        /// Закрытие позиции лонг (продажа)
        /// </summary>
        /// <param name="position">Позиция, которая будет закрыта</param>
        private void CloseLong(Position position)
        {
            decimal volumePosition = position.OpenVolume;
            
            // цена выхода из позиции (продаем дешевле)
            decimal priceClosePosition = _tab.PriceBestBid - Slippage.ValueInt * _tab.Securiti.PriceStep;

            // выход из позиции лонг по лимиту
            _tab.CloseAtLimit(position, priceClosePosition, volumePosition);
        }

        /// <summary>
        /// Закрытие позиции шорт (покупка)
        /// </summary>
        /// <param name="position"></param>
        private void CloseShort(Position position)
        {
            decimal volumePosition = position.OpenVolume;
            
            // цена выхода из позиции (покупаем дороже)
            decimal priceClosePosition = _tab.PriceBestAsk + Slippage.ValueInt * _tab.Securiti.PriceStep;

            // выход из позиции шорт по лимиту
            _tab.CloseAtLimit(position, priceClosePosition, volumePosition);
        }


        #endregion
    }
}
