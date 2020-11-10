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
    /// Interaction logic for OneLegArbitrageGridUi.xaml
    /// </summary>
    public partial class OneLegArbitrageGridUi : Window
    {
        private OneLegArbitrageGrid _bot;

        public OneLegArbitrageGridUi(OneLegArbitrageGrid bot)
        {
            _bot = bot;

            InitializeComponent();
            chbIsOn.IsChecked = _bot.IsOn;
            txbVolume.Text = _bot.Volume.ToString();
            txbMaxPositionsCount.Text = _bot.MaxPositionsCount.ToString();
            txbPositionsSpread.Text = _bot.PositionsSpread.ToString();
            txbMaxOrderDistance.Text = _bot.MaxOrderDistance.ToString();
            txbTradeTimePeriod.Text = _bot.TradeTimePeriod.ToString();
            txbSlippage.Text = _bot.Slippage.ToString();
            lblPriceStep.Content = _bot.PriceStep;

            // подписываемся на событие изменения фазы рынка для её вывода
            _bot.MarketFazeChangeEvent += _bot_MarketFazeChangeEvent;
        }

        private void _bot_MarketFazeChangeEvent(MarketFaze marketFaze)
        {
            if (lblMarketFaze.Dispatcher.CheckAccess() == false)
            {
                lblMarketFaze.Dispatcher.Invoke(new Action<MarketFaze>(_bot_MarketFazeChangeEvent), marketFaze);
                return;
            }
            
            lblMarketFaze.Content = marketFaze.ToString();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            _bot.IsOn = chbIsOn.IsChecked.Value;

            try
            {
                _bot.MaxPositionsCount = Convert.ToInt32(txbMaxPositionsCount.Text);
                _bot.PositionsSpread = Convert.ToInt32(txbPositionsSpread.Text);
                _bot.MaxOrderDistance = Convert.ToInt32(txbMaxOrderDistance.Text);
                _bot.TradeTimePeriod = Convert.ToInt32(txbTradeTimePeriod.Text);
                _bot.Slippage = Convert.ToInt32(txbSlippage.Text);
            }
            catch (Exception)
            {
                MessageBox.Show("Error settings paramerts");
                return;
            }

            _bot.Save();
            //Close();
        }
    }
}
