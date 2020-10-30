using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OsEngine.Robots.MyBot
{
    /// <summary>
    /// Логика взаимодействия для SpatialArbitrageUi.xaml
    /// </summary>
    public partial class SpatialArbitrageUi : Window
    {
        private SpatialArbitrage _bot;
        
        public SpatialArbitrageUi(SpatialArbitrage bot)
        {
            InitializeComponent();
            // ссылка на робота
            _bot = bot;

            // событие изменения текущего профита
            _bot.CurrentProfitChangeEvent += _bot_CurrentProfitChangeEvent;

            // берем из робота текущие настроечные параметры
            // и выводим их в окно настроечных параметров
            chbIsOn.IsChecked = _bot.IsOn;
            txbVolume.Text = _bot.Volume.ToString();
            txbMinProfit.Text = _bot.MinProfit.ToString();
            txbComissionPct.Text = _bot.ComissionPct.ToString();
            txbDecimals.Text = _bot.Decimals.ToString();
        }
        
        /// <summary>
        /// Обработчик события изменения текущего профита
        /// </summary>
        /// <param name="currentProfit"></param>
        private void _bot_CurrentProfitChangeEvent(decimal currentProfit)
        {
            if (txbCurrentProfit.Dispatcher.CheckAccess() == false)
            {
                txbCurrentProfit.Dispatcher.Invoke(new Action<decimal> (_bot_CurrentProfitChangeEvent),currentProfit);
                return;
            }

            // выводим текущий профит
            txbCurrentProfit.Text = currentProfit.ToString();
        }

        /// <summary>
        /// Обработчик события клика по кнопке Accept
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // переносим в робота заданные пользователем настроечные параметры
                if (chbIsOn.IsChecked != null) _bot.IsOn = chbIsOn.IsChecked.Value;
                _bot.Volume = Convert.ToDecimal(txbVolume.Text);
                _bot.MinProfit = Convert.ToDecimal(txbMinProfit.Text);
                _bot.ComissionPct = Convert.ToDecimal(txbComissionPct.Text);
                _bot.Decimals = Convert.ToInt32(txbDecimals.Text);

            }
            catch (Exception exc)
            {
                MessageBox.Show($"Error input parametrs");
                return;
            }
           
            _bot.Save();
            // Close();
        }
    }
}
