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

namespace ModBusServerConfigurator
{
    /// <summary>
    /// Interaction logic for AddProfile.xaml
    /// </summary>
    public partial class AddProfile : Window
    {
        public string fileName = "";

        public AddProfile()
        {
            InitializeComponent();

            // Centraggio finestra
            this.Left = (System.Windows.SystemParameters.PrimaryScreenWidth / 2) - (this.Width / 2);
            this.Top = (System.Windows.SystemParameters.PrimaryScreenHeight / 2) - (this.Height / 2);
        }

        private void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            fileName = TextBoxProfile.Text;
            this.DialogResult = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TextBoxProfile.Focus();
        }

        private void TextBoxProfile_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
                ButtonSave_Click(null, null);
            }
        }
    }
}
