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
using OsEngine.Robots.MyBot;
using OsEngine.Entity;

namespace OsEngine.Robots.MyBot
{
    /// <summary>
    /// Interaction logic for PairArbitrageUi.xaml
    /// </summary>
    public partial class PairArbitrageUi : Window
    {
        PairArbitrage _bot;

        public PairArbitrageUi(PairArbitrage bot)
        {
            _bot = bot;
            InitializeComponent();

            chbIsOn.IsChecked = _bot.IsOn;
            cmbWhoIsFirst.Items.Add(WhoIsFirst.First.ToString());
            cmbWhoIsFirst.Items.Add(WhoIsFirst.Second.ToString());
            cmbWhoIsFirst.Items.Add(WhoIsFirst.Nobody.ToString());
            cmbWhoIsFirst.SelectedItem = _bot.WhoIsFirst.ToString();
            txbMultiply.Text = _bot.Multiply.ValueDecimal.ToString();

            cmbSide1.Items.Add(Side.Buy.ToString());
            cmbSide1.Items.Add(Side.Sell.ToString());
            cmbSide1.SelectedItem = _bot.Side1.ToString();
            cmbSide2.Items.Add(Side.Buy.ToString());
            cmbSide2.Items.Add(Side.Sell.ToString());
            cmbSide2.SelectedItem = _bot.Side2.ToString();

            txbVolume1.Text = _bot.Volume1.ToString();
            txbVolume2.Text = _bot.Volume2.ToString();

            txbSlippage1.Text = _bot.Slippage1.ToString();
            txbSlippage2.Text = _bot.Slippage2.ToString();


        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            _bot.IsOn = chbIsOn.IsChecked.Value;
            Enum.TryParse(cmbWhoIsFirst.SelectedItem.ToString(), out _bot.WhoIsFirst);
            Enum.TryParse(cmbSide1.SelectedItem.ToString(), out _bot.Side1);
            Enum.TryParse(cmbSide2.SelectedItem.ToString(), out _bot.Side2);
            try
            {
                _bot.Multiply.ValueDecimal = Convert.ToDecimal(txbMultiply.Text);
                _bot.Volume1 = Convert.ToDecimal(txbVolume1.Text);
                _bot.Volume2 = Convert.ToDecimal(txbVolume2.Text);
                _bot.Slippage1 = Convert.ToInt32(txbSlippage1.Text);
                _bot.Slippage2 = Convert.ToInt32(txbSlippage2.Text);
            }
            catch
            {
                MessageBox.Show("Error input parametrs");
                return;
            }

            _bot.Save();
            Close();
        }
    }
}
