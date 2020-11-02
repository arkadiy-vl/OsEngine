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
using Microsoft.Win32;

namespace OneLegArbitrageMy
{
    /// <summary>
    /// Логика взаимодействия для OneLegArbitrageMyUi.xaml
    /// </summary>
    public partial class OneLegArbitrageMyUi : Window
    {
        private OneLegArbitrageMy _bot;

        public OneLegArbitrageMyUi(OneLegArbitrageMy bot)
        {
            InitializeComponent();
            _bot = bot;

            // переносим текущие параметры робота в окно настроечных параметров
            chbIsOn.IsChecked = _bot.IsOn;
            chbOnFixedDeposit.IsChecked = _bot.OnFixedVolume;
            txbFixedVolume.Text = _bot.FixedVolume.ToString();
            txbVolumePct.Text = _bot.VolumePctOfDeposit.ToString();
            txbSlippage.Text = _bot.Slippage.ToString();
            txbVolumeDecimals.Text = _bot.VolumeDecimals.ToString();
            cmbChannelIndicator.Items.Add(ChannelIndicator.Bollinger);
            cmbChannelIndicator.Items.Add(ChannelIndicator.MA_ATR);
            cmbChannelIndicator.SelectedItem = _bot.ChannelIndicator;
            cmbMethodOfExit.Items.Add(MethodOfExit.BoundaryChannel);
            cmbMethodOfExit.Items.Add(MethodOfExit.CenterChannel);
            cmbMethodOfExit.SelectedItem = _bot.MethodOfExit;
            txbMaxPositionsCount.Text = _bot.MaxPositionsCount.ToString();
            txbDeviationIndexForAddEnter.Text = _bot.DeviationIndexForAddEnter.ToString();

            // подписываемся на событие изменения фазы рынка
            _bot.MarketFazeChangeEvent += _bot_MarketFazeChangeEvent;
        }

        private void _bot_MarketFazeChangeEvent(MarketFaze marketFaze)
        {
            if (lblMarketFaze.Dispatcher.CheckAccess() == false)
            {
                lblMarketFaze.Dispatcher.Invoke(new Action<MarketFaze>(_bot_MarketFazeChangeEvent), marketFaze);
                return;
            }
            
            // выводим текущую фазу рынка
            lblMarketFaze.Content = marketFaze.ToString();
        }

        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _bot.IsOn = chbIsOn.IsChecked.Value;
                _bot.OnFixedVolume = chbOnFixedDeposit.IsChecked.Value;
                _bot.FixedVolume = Convert.ToDecimal(txbFixedVolume.Text);
                _bot.VolumePctOfDeposit = Convert.ToInt32(txbVolumePct.Text);
                _bot.Slippage = Convert.ToInt32(txbSlippage.Text);
                _bot.VolumeDecimals = Convert.ToInt32(txbVolumeDecimals.Text);
                Enum.TryParse(cmbChannelIndicator.SelectedItem.ToString(), out _bot.ChannelIndicator);
                Enum.TryParse(cmbMethodOfExit.SelectedItem.ToString(), out _bot.MethodOfExit);
                _bot.MaxPositionsCount = Convert.ToInt32(txbMaxPositionsCount.Text);
                _bot.DeviationIndexForAddEnter = Convert.ToDecimal(txbDeviationIndexForAddEnter.Text);
            }
            catch (Exception exception)
            {
                MessageBox.Show("Error input parametrs");
                return;
            }

            _bot.Save();
            //Close();
        }
    }
}
