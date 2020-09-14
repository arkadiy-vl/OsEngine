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
using OsEngine.Entity;
using OsEngine.Market;
using OsEngine.Market.Servers;

namespace OsEngine.Robots.MyBot
{
    /// <summary>
    /// Логика взаимодействия для HftEngineUi.xaml
    /// </summary>
    public partial class HftEngineUi : Window
    {
        HftEngine _bot;
        
        public HftEngineUi(HftEngine bot)
        {
            InitializeComponent();
            _bot = bot;

            // подписка на события
            ServerMaster.ServerCreateEvent += ServerMaster_ServerCreateEvent;
            chbIsOn.Click += ChbIsOn_Click;
            txbOrderLifeTime.TextChanged += TxbOrderTimeLife_TextChanged;
            txbStop.TextChanged += TxbStop_TextChanged;
            txbProfit.TextChanged += TxbProfit_TextChanged;

            UpdateCmbServer();
            UpdateCmbBoxes();

            chbIsOn.IsChecked = _bot.IsOnPositionSupport;
            txbOrderLifeTime.Text = _bot.OrderLifeTime.ToString();
            txbStop.Text = _bot.Stop.ToString();
            txbProfit.Text = _bot.Profit.ToString();

        }

        #region Обработчики событий
        private void ChbIsOn_Click(object sender, RoutedEventArgs e)
        {
            _bot.IsOnPositionSupport = chbIsOn.IsChecked.Value;
            _bot.Save();
        }

        private void TxbOrderTimeLife_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _bot.OrderLifeTime = Convert.ToInt32(txbOrderLifeTime.Text);
                _bot.Save();
            }
            catch (Exception)
            {
                MessageBox.Show("Error parsing settings parametrs.");
            }
        }

        private void TxbStop_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _bot.Stop = Convert.ToInt32(txbStop.Text);
                _bot.Save();
            }
            catch (Exception)
            {
                MessageBox.Show("Error parsing settings parametrs.");
            }
        }

        private void TxbProfit_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _bot.Profit = Convert.ToInt32(txbProfit.Text);
                _bot.Save();
            }
            catch (Exception)
            {
                MessageBox.Show("Error parsing settings parametrs.");
            }
        }

        private void ServerMaster_ServerCreateEvent(IServer server)
        {
            UpdateCmbServer();
        }

        private void UpdateCmbServer()
        {
            // проверяем, что метод вызывает поток, который создавал этот комбобокс,
            // если нет, то вызываем метод из главного потока 
            if(cmbServer.Dispatcher.CheckAccess() == false)
            {
                cmbServer.Dispatcher.Invoke(UpdateCmbServer);
                return;
            }

            // очищаем комбобокс
            cmbServer.Items.Clear();

            // получаем все доступные сервера
            List<IServer> allServers = ServerMaster.GetServers();

            // переносим в комбобокс все доступные сервера
            for (int i = 0; allServers != null && i < allServers.Count; i++)
            {
                cmbServer.Items.Add(allServers[i].ServerType.ToString());
            }

            UpdateCmbBoxes();
        }

        private void UpdateCmbBoxes()
        {
            cmbPortfolio.Items.Clear();
            cmbSecurity.Items.Clear();

            for (int i = 0; _bot.Portfolios != null && i < _bot.Portfolios.Count; i++)
            {
                cmbPortfolio.Items.Add(_bot.Portfolios[i].Number);
            }

            for (int i = 0; _bot.Securities != null && i < _bot.Securities.Count; i++)
            {
                cmbSecurity.Items.Add(_bot.Securities[i].Name);
            }

        }

        private void btnBuy_Click(object sender, RoutedEventArgs e)
        {
            OpenOrder(Side.Buy);
        }

        private void btnSell_Click(object sender, RoutedEventArgs e)
        {
            OpenOrder(Side.Sell);
        }

        private void btnRejectOrders_Click(object sender, RoutedEventArgs e)
        {
            _bot.RejectAllOrders();
        }

        #endregion

        private void OpenOrder(Side sideOrder)
        {
            if (cmbServer.SelectedItem == null ||
               cmbServer.SelectedItem.ToString() == "" ||
               cmbPortfolio.SelectedItem == null ||
               cmbPortfolio.SelectedItem.ToString() == "" ||
               cmbSecurity.SelectedItem == null ||
               cmbSecurity.SelectedItem.ToString() == "")
            {
                MessageBox.Show("Error settings parametrs");
                return;
            }

            ServerType serverType;
            string portfolio;
            string security;
            decimal price;
            decimal volume;

            Enum.TryParse(cmbServer.SelectedItem.ToString(), out serverType);
            portfolio = cmbPortfolio.SelectedItem.ToString();
            security = cmbSecurity.SelectedItem.ToString();

            try
            {
                price = Convert.ToDecimal(txbPrice);
                volume = Convert.ToDecimal(txbVolume);
            }
            catch (Exception)
            {
                MessageBox.Show("Error settings parametrs");
                return;
            }

            _bot.SendOrder(serverType, portfolio, security, price, volume, sideOrder);
        }
    }
}
