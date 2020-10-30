using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;

namespace OsEngine.Robots.MyBot
{
    [Bot("SpatialArbitrage")]
    public class SpatialArbitrage : BotPanel
    {
        #region=== Параметры робота ====
        // настроечные параметры робота
        public bool IsOn = false;
        public decimal CurrentProfit;
        public decimal Volume = 50;
        public decimal MinProfit = 1;
        public decimal ComissionPct = 0.75m;
        public int Decimals = 4;

        // внутренние параметры робота
        private DateTime _lastTradeTime = DateTime.Now.AddYears(-10);
        private int TradeTimePeriod = 1;
        
        // вкладки для торговли
        private BotTabSimple _tab1;
        private BotTabSimple _tab2;
        private BotTabSimple _tab3;
        private BotTabSimple _tab4;

        // событие изменения текущего профита
        public event Action<decimal> CurrentProfitChangeEvent;

        #endregion

        /// <summary>
        /// Конструктор класса робота
        /// </summary>
        /// <param name="name"></param>
        /// <param name="startProgram"></param>
        public SpatialArbitrage(string name, StartProgram startProgram) : base(name, startProgram)
        {
            // создаем вкладки
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Simple);
            TabCreate(BotTabType.Simple);

            _tab1 = TabsSimple[0];
            _tab2 = TabsSimple[1];
            _tab3 = TabsSimple[2];
            _tab4 = TabsSimple[3];

            // загружаем настроечные параметры
            Load();

            // подписываемся на события
            _tab1.ServerTimeChangeEvent += _tab1_ServerTimeChangeEvent;
            _tab1.PositionNetVolumeChangeEvent += _tab1_PositionNetVolumeChangeEvent;
            _tab2.PositionNetVolumeChangeEvent += _tab2_PositionNetVolumeChangeEvent;
            _tab3.PositionNetVolumeChangeEvent += _tab3_PositionNetVolumeChangeEvent;
            _tab4.PositionNetVolumeChangeEvent += _tab4_PositionNetVolumeChangeEvent;

            DeleteEvent += SpatialArbitrage_DeleteEvent;

        }

        #region === Сервисная логика ===
        /// <summary>
        /// Сервисный метод получения имени стратегии робота
        /// </summary>
        /// <returns></returns>
        public override string GetNameStrategyType()
        {
            return "SpatialArbitrage";
        }

        /// <summary>
        /// Сервисный метод вывода окна настроек робота
        /// </summary>
        public override void ShowIndividualSettingsDialog()
        {
            SpatialArbitrageUi ui = new SpatialArbitrageUi(this);
            ui.Show();
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
                    writer.WriteLine(Volume);
                    writer.WriteLine(MinProfit);
                    writer.WriteLine(ComissionPct);
                    writer.WriteLine(Decimals);

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
                    Volume = Convert.ToDecimal(reader.ReadLine());
                    MinProfit = Convert.ToDecimal(reader.ReadLine());
                    ComissionPct = Convert.ToDecimal(reader.ReadLine());
                    Decimals = Convert.ToInt32(reader.ReadLine());

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
        private void SpatialArbitrage_DeleteEvent()
        {
            if (File.Exists(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"SettingsBot.txt");
            }
        }

        #endregion

        #region === Торговая логика ===
        private void _tab1_ServerTimeChangeEvent(DateTime time)
        {
            // запускаем торговую логику только через периоды времени _tradeTimePeriod 
            if (_lastTradeTime.AddSeconds(TradeTimePeriod) > time)
            {
                return;
            }
            _lastTradeTime = time;

            // получаем текущий профит для цикла перелива
            CurrentProfit = GetCurrentProfit();

            // Если кто-то подписан на событие изменения текущего профита,
            // то выдаем текущий профит
            if (CurrentProfitChangeEvent != null)
            {
                CurrentProfitChangeEvent(CurrentProfit);
            }

            if (!IsOn) return;

            // если есть открытые или открываемые позиции, то ничего не делаем
            if (_tab1.PositionsOpenAll.Count > 0 ||
                _tab2.PositionsOpenAll.Count > 0 ||
                _tab3.PositionsOpenAll.Count > 0 ||
                _tab4.PositionsOpenAll.Count > 0)
            {
                return;
            }

            // если текущий профит превысил заданный профит,
            // то запускаем цикл перелива - покупаем инструмент 1 лимитной заявкой
            if (CurrentProfit > MinProfit)
            {
                decimal buyPrice1 = _tab1.PriceBestAsk;
                decimal volume1 = Math.Round(Volume / buyPrice1, Decimals);
                _tab1.BuyAtLimit(volume1, buyPrice1);
            }
        }

        /// <summary>
        /// Получить текущий профит для цикла перелива
        /// </summary>
        /// <returns></returns>
        private decimal GetCurrentProfit()
        {
            if (_tab1.IsConnected == false ||
                _tab2.IsConnected == false ||
                _tab3.IsConnected == false ||
                _tab4.IsConnected == false)
            {
                return -9999;
            }

            decimal buyPrice1 = _tab1.PriceBestAsk;
            decimal sellPrice2 = _tab2.PriceBestBid;
            decimal buyPrice3 = _tab3.PriceBestAsk;
            decimal sellPrice4 = _tab4.PriceBestBid;

            if (buyPrice1 <= 0 ||
                sellPrice2 <= 0 ||
                buyPrice3 <= 0 ||
                sellPrice4 <= 0)
            {
                return -9999;
            }

            decimal volume1 = (Volume / buyPrice1) * (1 - ComissionPct/100.0m);
            decimal volume2 = (volume1 * sellPrice2) * (1 - ComissionPct/100.0m);
            decimal volume3 = (volume2 / buyPrice3) * (1 - ComissionPct/100.0m);
            decimal volume4 = (volume3 * sellPrice4) * (1 - ComissionPct/100.0m);

            return Math.Round(volume4 - Volume, 4);
        }

        /// <summary>
        /// Обработчик события изменения нетто позиции для инструмента 1
        /// </summary>
        /// <param name="position"></param>
        private void _tab1_PositionNetVolumeChangeEvent(Position position)
        {
            if (position.WaitVolume != 0)
            {
                return;
            }

            decimal sellPrice2 = _tab2.PriceBestBid;
            decimal volume2 = Math.Round(position.OpenVolume * sellPrice2, Decimals);

            _tab2.SellAtLimit(position.OpenVolume, sellPrice2, volume2.ToString());

            _tab1.GetJournal().DeletePosition(position);
            _tab1.SetNewLogMessage($"tab1 buy, sec1 {position.OpenVolume}", LogMessageType.System);
        }

        private void _tab2_PositionNetVolumeChangeEvent(Position position)
        {
            if (position.WaitVolume != 0)
            {
                return;
            }

            decimal volume2 = Convert.ToDecimal(position.SignalTypeOpen);
            decimal buyPrice3 = _tab3.PriceBestAsk;
            decimal volume3 = Math.Round(volume2 / buyPrice3, Decimals);
            
            _tab3.BuyAtLimit(volume3, buyPrice3);

            _tab2.GetJournal().DeletePosition(position);
            _tab1.SetNewLogMessage($"tab2 sell, sec2 {volume2}", LogMessageType.System);
        }

        private void _tab3_PositionNetVolumeChangeEvent(Position position)
        {
            if (position.WaitVolume != 0)
            {
                return;
            }

            decimal sellPrice4 = _tab4.PriceBestBid;
            decimal volume4 = Math.Round(position.OpenVolume * sellPrice4, Decimals);

            _tab4.SellAtLimit(position.OpenVolume, sellPrice4, volume4.ToString());

            _tab3.GetJournal().DeletePosition(position);
            _tab1.SetNewLogMessage($"tab3 buy, sec3 {position.OpenVolume}", LogMessageType.System);
        }

        private void _tab4_PositionNetVolumeChangeEvent(Position position)
        {
            if (position.WaitVolume != 0)
            {
                return;
            }

            decimal volume4 = Convert.ToDecimal(position.SignalTypeOpen);

            _tab4.GetJournal().DeletePosition(position);
            _tab1.SetNewLogMessage($"tab4 sell, sec4 {volume4}, profit {volume4-Volume}", LogMessageType.System);
        }


        #endregion
    }
}
