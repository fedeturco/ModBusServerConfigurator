
// Copyright (c) 2021 Federico Turco

// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:

// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Web.Script.Serialization;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Microsoft.Win32;

// Visualizza/Nascondi console
using System.Runtime.InteropServices;

using Renci.SshNet;

namespace ModBusServerConfigurator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MOD_Config ActualConfig = new MOD_Config();

        Thread modbusThread;
        public bool modbusThreadRunning = false;    // Stato runnning windows
        Thread logDequeueThread;
        Process python_gw;
        public bool modbusRaspberryRunning = false;

        dynamic ClientConfig;

        // Elementi per visualizzare/nascondere la finestra della console
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        public bool consoleIsOpen = true;

        // Disable Console Exit Button
        [DllImport("user32.dll")]
        static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        [DllImport("user32.dll")]
        static extern IntPtr DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

        const uint SC_CLOSE = 0xF060;
        const uint MF_BYCOMMAND = (uint)0x00000000L;

        bool connected = false;
        SshClient sshClient;

        FixedSizedQueue<string> BufferLog = new FixedSizedQueue<string>(100000);

        public int currentMode = 0;

        public bool disableRichTextBoxesLog = false;
        public bool disableAutoscroll = false;

        public string oldProfileCombo = null; // Variabile di appoggio per salvare il vecchio profilo quando un utente cambia

        public uint rowMaxLog = 10;
        public int pingTimeout = 100;
        public MainWindow()
        {
            InitializeComponent();

            // Carico il file di configurazione del client (contiene gli id da salvare alla chiusura)
            JavaScriptSerializer jss = new JavaScriptSerializer();
            ClientConfig = jss.DeserializeObject(File.ReadAllText("Json\\ClientConfig.json"));

            // Centraggio finestra
            this.Left = (System.Windows.SystemParameters.PrimaryScreenWidth / 2) - (this.Width / 2);
            this.Top = (System.Windows.SystemParameters.PrimaryScreenHeight / 2) - (this.Height / 2);

            // Diabilito il pulsante di chiusura della console
            disableConsoleExitButton();

            // Chiudo console all'avvio
            chiudiConsole();
        }

        // Visualizza console programma da menu tendina
        public void apriConsole()
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_SHOW);

            consoleIsOpen = true;
        }

        // Nasconde console programma da menu tendina
        public void chiudiConsole()
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            consoleIsOpen = false;
        }

        // Disabilita il pulsante di chiusura della console
        public void disableConsoleExitButton()
        {
            IntPtr handle = GetConsoleWindow();
            IntPtr exitButton = GetSystemMenu(handle, false);
            if (exitButton != null) DeleteMenu(exitButton, SC_CLOSE, MF_BYCOMMAND);
        }

        public void readConfig(string path)
        {
            JavaScriptSerializer jss = new JavaScriptSerializer();
            dynamic tmp = jss.Deserialize<dynamic>(File.ReadAllText(path));

            if (!tmp.ContainsKey("type"))
            {
                // MessageBox.Show("Configuration file is not a valid json for this application", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                MessageBox.Show("Il file selezionato non è un json di configurazione valido", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (tmp["type"] != "ModBusServerConfig")
            {
                // MessageBox.Show("Configuration file is not a valid json for this application", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                MessageBox.Show("Il file selezionato non è un json di configurazione valido", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActualConfig = new MOD_Config();

            foreach (KeyValuePair<string, object> levels in tmp["modBus"])
            {
                switch (levels.Key)
                {
                    case "debug":
                        ActualConfig.debug = (bool)levels.Value;
                        break;

                    case "assignIp":
                        ActualConfig.assignIp = (bool)levels.Value;
                        break;

                    // Carico testa TCP
                    case "TCP":
                        ActualConfig.TCP = new List<MOD_HeadTcp>();

                        foreach (dynamic din in tmp["modBus"]["TCP"])
                        {
                            MOD_HeadTcp head = new MOD_HeadTcp();

                            // Controllo l'esistenza della chiave enable
                            if (din.ContainsKey("enable"))
                            {
                                head.enable = din["enable"];
                            }
                            else
                            {
                                head.enable = false;
                            }

                            // Le seguenti devono esistere per forza
                            head.label = din["label"];
                            head.ip_address = din["ip_address"];
                            head.port = din["port"];

                            // Controllo l'esistenza della chiave notes
                            if (din.ContainsKey("notes"))
                            {
                                head.notes = din["notes"];
                            }
                            else
                            {
                                head.notes = "";
                            }

                            head.slaves = new List<string>();

                            if (din.ContainsKey("slaves"))
                            {
                                if (din["slaves"] != null)
                                {
                                    foreach (string prof in din["slaves"])
                                    {
                                        head.slaves.Add(prof);

                                        // debug
                                        //Console.WriteLine("slave: " + prof);
                                    }
                                }
                            }

                            // debug
                            //Console.WriteLine("\nlabel: " + head.label);
                            //Console.WriteLine("enable: " + head.enable.ToString());
                            //Console.WriteLine("ip_address: " + head.ip_address);
                            //Console.WriteLine("port: " + head.port);
                            //Console.WriteLine("slaves: " + head.slaves);

                            ActualConfig.TCP.Add((MOD_HeadTcp)(head));
                        }
                        break;

                    // Carico testa RTU
                    case "RTU":
                        ActualConfig.RTU = new List<MOD_HeadRtu>();

                        foreach (dynamic din in tmp["modBus"]["RTU"])
                        {
                            MOD_HeadRtu head = new MOD_HeadRtu();

                            // Controllo l'esistenza della chiave enable
                            if (din.ContainsKey("enable"))
                            {
                                head.enable = din["enable"];
                            }
                            else
                            {
                                head.enable = false;
                            }

                            // Le seguenti devono esistere per forza
                            head.label = din["label"];
                            head.serial = din["serial"];
                            head.baudrate = din["baudrate"];
                            head.config = din["config"];

                            // Controllo l'esistenza della chiave notes
                            if (din.ContainsKey("notes"))
                            {
                                head.notes = din["notes"];
                            }
                            else
                            {
                                head.notes = "";
                            }

                            head.slaves = new List<string>();

                            // debug
                            //Console.WriteLine("\nlabel: " + head.label);
                            //Console.WriteLine("enable: " + head.enable.ToString());
                            //Console.WriteLine("serial: " + head.serial);
                            //Console.WriteLine("baudrate: " + head.baudrate);
                            //Console.WriteLine("config: " + head.config);

                            if (din.ContainsKey("slaves"))
                            {
                                if (din["slaves"] != null)
                                {
                                    foreach (string prof in din["slaves"])
                                    {
                                        head.slaves.Add(prof);

                                        // debug
                                        //Console.WriteLine("slave: " + prof);
                                    }
                                }
                            }

                            ActualConfig.RTU.Add((MOD_HeadRtu)(head));
                        }
                        break;

                    // Carico profili
                    case "profiles":
                        ActualConfig.profiles = new List<MOD_SlaveProfile>();

                        foreach (dynamic din in tmp["modBus"]["profiles"])
                        {
                            MOD_SlaveProfile slave = new MOD_SlaveProfile();

                            slave.label = din["label"];

                            if (din.ContainsKey("notes"))
                            {
                                if (din["notes"] != null)
                                    slave.notes = din["notes"];
                                else
                                    slave.notes = "";
                            }
                            else
                            {
                                slave.notes = "";
                            }

                            if (din.ContainsKey("type"))
                            {
                                if(din["type"] != null)
                                    slave.type = din["type"];
                                else
                                    slave.type = "ModBusSlave";
                            }
                            else
                            {
                                slave.type = "ModBusSlave";
                            }

                            slave.slave_id = new List<int>();

                            foreach(int slaveId in din["slave_id"])
                            {
                                slave.slave_id.Add(slaveId);
                            }

                            string[] keys = new string[] { "di", "co", "ir", "hr" };

                            foreach (string key in keys)
                            {
                                MOD_RegProfile di = new MOD_RegProfile();
                                di.len = din[key]["len"];
                                di.data = new List<MOD_Reg>();

                                List<MOD_Reg> list = new List<MOD_Reg>();

                                foreach (dynamic regConfig in din[key]["data"])
                                {
                                    MOD_Reg reg = new MOD_Reg();

                                    reg.reg = regConfig["reg"];
                                    reg.label = regConfig["label"];
                                    reg.value = (UInt16)(regConfig["value"]);

                                    di.data.Add(reg);
                                }

                                switch (key)
                                {
                                    case "di":
                                        slave.di = di;
                                        break;

                                    case "co":
                                        slave.co = di;
                                        break;

                                    case "ir":
                                        slave.ir = di;
                                        break;

                                    case "hr":
                                        slave.hr = di;
                                        break;
                                }
                            }

                            ActualConfig.profiles.Add(slave);
                        }
                        break;
                }
            }
            
            DataGridHeadTcp.ItemsSource = ActualConfig.TCP;
            DataGridHeadRtu.ItemsSource = ActualConfig.RTU;

            AggiornaProfiliListAndComboBox();

            // Seleziono il primo profilo disponibile per non lasciare la comboBox vuota
            ComboBoxProfiles.SelectedIndex = 0;

            CheckBoxDebug.IsChecked = ActualConfig.debug;
            CheckBoxAddAddressEth0.IsChecked = ActualConfig.assignIp;
        }

        public void writeConfig(string path)
        {
            Dictionary<string, Object> toSave = new Dictionary<string, Object>();
            Dictionary<string, Object> modBus = new Dictionary<string, Object>();

            // Triggero la variazione così salvo le variabili associate al profilo
            ComboBoxProfiles_SelectionChanged(null, null);

            // debug
            //modBus.Add("debug", ActualConfig.debug);
            //modBus.Add("TCP", ActualConfig.TCP);
            //modBus.Add("RTU", ActualConfig.RTU);
            //modBus.Add("profiles", ActualConfig.profiles);

            toSave.Add("type", "ModBusServerConfig");
            toSave.Add("modBus", ActualConfig);

            JavaScriptSerializer jss = new JavaScriptSerializer();
            File.WriteAllText(path, jss.Serialize(toSave));
        }

        public void saveConfig()
        {
            Dictionary<string, Object> toSave = new Dictionary<string, Object>();

            foreach (KeyValuePair<string, dynamic> idToSave in ClientConfig["toSave"])
            {
                // debug
                //Console.WriteLine("Key: " + idToSave.Key);
                //Console.WriteLine("Value: " + idToSave.Value);

                switch (idToSave.Key)
                {
                    case "textBoxes":

                        Dictionary<string, string> textBoxes = new Dictionary<string, string>();

                        foreach (KeyValuePair<string, dynamic> obj in idToSave.Value)
                        {
                            // debug
                            //Console.WriteLine(" -Key: " + obj.Key);
                            //Console.WriteLine(" -Value: " + obj.Value);

                            TextBox textBox = (TextBox)this.FindName(obj.Key);

                            textBoxes.Add(obj.Key, textBox.Text);   // IdTextBox, Value
                        }

                        toSave.Add("textBoxes", textBoxes);

                        break;

                    case "checkBoxes":

                        Dictionary<string, bool> checkBoxes = new Dictionary<string, bool>();

                        foreach (KeyValuePair<string, dynamic> obj in idToSave.Value)
                        {
                            // debug
                            //Console.WriteLine(" -Key: " + obj.Key);
                            //Console.WriteLine(" -Value: " + obj.Value);

                            CheckBox checkBox = (CheckBox)this.FindName(obj.Key);

                            checkBoxes.Add(obj.Key, (bool)checkBox.IsChecked);   // IdTextBox, Value
                        }

                        toSave.Add("checkBoxes", checkBoxes);

                        break;

                    case "radioButtons":

                        Dictionary<string, bool> radioButtons = new Dictionary<string, bool>();

                        foreach (KeyValuePair<string, dynamic> obj in idToSave.Value)
                        {
                            // debug
                            //Console.WriteLine(" -Key: " + obj.Key);
                            //Console.WriteLine(" -Value: " + obj.Value);

                            RadioButton radioButton = (RadioButton)this.FindName(obj.Key);

                            radioButtons.Add(obj.Key, (bool)radioButton.IsChecked);   // IdTextBox, Value
                        }

                        toSave.Add("radioButtons", radioButtons);

                        break;
                }
            }

            JavaScriptSerializer jss = new JavaScriptSerializer();
            File.WriteAllText("Json\\Settings.json", jss.Serialize(toSave));
        }

        public void loadConfig()
        {
            if (File.Exists(Directory.GetCurrentDirectory() + "\\Json\\Settings.json"))
            {
                JavaScriptSerializer jss = new JavaScriptSerializer();
                dynamic file_CFG = jss.DeserializeObject(File.ReadAllText(Directory.GetCurrentDirectory() + "\\Json\\Settings.json"));

                foreach (KeyValuePair<string, object> firstLevel in file_CFG)
                {
                    switch (firstLevel.Key)
                    {
                        case "textBoxes":
                            foreach (KeyValuePair<string, object> textBoxes in file_CFG["textBoxes"])
                            {
                                TextBox textBox = (TextBox)this.FindName(textBoxes.Key);
                                textBox.Text = textBoxes.Value.ToString();
                            }
                            break;

                        case "checkBoxes":
                            foreach (KeyValuePair<string, object> checkBoxes in file_CFG["checkBoxes"])
                            {
                                CheckBox checkBox = (CheckBox)this.FindName(checkBoxes.Key);
                                checkBox.IsChecked = (bool)checkBoxes.Value;
                            }
                            break;

                        case "radioButtons":
                            foreach (KeyValuePair<string, object> radioButtons in file_CFG["radioButtons"])
                            {
                                RadioButton radioButton = (RadioButton)this.FindName(radioButtons.Key);
                                radioButton.IsChecked = (bool)radioButtons.Value;
                            }
                            break;
                    }
                }
            }
        }

        private void ComboBoxProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ComboBoxProfiles.SelectedItem != null)
                {
                    string profile = ComboBoxProfiles.SelectedItem.ToString();

                    if (oldProfileCombo != null)
                    {
                        if (ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo) != null)
                        {

                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).co.len = int.Parse(TextBoxCoilsLen.Text);
                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).di.len = int.Parse(TextBoxInputsLen.Text);
                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).ir.len = int.Parse(TextBoxInputRegisterLen.Text);
                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).hr.len = int.Parse(TextBoxHoldingRegisterLen.Text);

                            List<int> tmp1 = new List<int>();
                            foreach (string tmp2 in TextBoxDeviceId.Text.Split(','))
                                tmp1.Add(int.Parse(tmp2));
                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).slave_id = new List<int>(tmp1);

                            TextRange textRange = new TextRange(RichTextBoxNotesSlave.Document.ContentStart, RichTextBoxNotesSlave.Document.ContentEnd);
                            ActualConfig.profiles.Find(tmp => tmp.label == oldProfileCombo).notes = textRange.Text;
                        }
                    }

                    oldProfileCombo = profile;

                    try
                    {
                        DataGridInputs.ItemsSource = null;
                        DataGridCoils.ItemsSource = null;
                        DataGridInputRegisters.ItemsSource = null;
                        DataGridHoldingRegisters.ItemsSource = null;

                        MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                        if (slave != null)
                        {
                            TextBoxCoilsLen.Text = slave.co.len.ToString();
                            TextBoxInputsLen.Text = slave.di.len.ToString();
                            TextBoxInputRegisterLen.Text = slave.ir.len.ToString();
                            TextBoxHoldingRegisterLen.Text = slave.hr.len.ToString();

                            // Converto lista in csv string
                            string tmp = "";
                            for(int i  = 0; i < slave.slave_id.Count; i++)
                            {
                                tmp += slave.slave_id[i].ToString();

                                if (i < (slave.slave_id.Count - 1))
                                    tmp += ",";
                            }
                            TextBoxDeviceId.Text = tmp;

                            RichTextBoxNotesSlave.Document.Blocks.Clear();

                            if (slave.notes == null)
                            {
                                slave.notes = "";
                            }

                            RichTextBoxNotesSlave.AppendText(slave.notes.ToString());

                            DataGridInputs.ItemsSource = slave.di.data;
                            DataGridCoils.ItemsSource = slave.co.data;
                            DataGridInputRegisters.ItemsSource = slave.ir.data;
                            DataGridHoldingRegisters.ItemsSource = slave.hr.data;
                        }
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err);
                    }
                }
                else
                {
                    oldProfileCombo = null;
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void RadioButtonWindows_Checked(object sender, RoutedEventArgs e)
        {
            StackPanelWindows.Visibility = (bool)RadioButtonWindows.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            StackPanelLinux.Visibility = (bool)RadioButtonWindows.IsChecked ? Visibility.Collapsed : Visibility.Visible;

            CheckBoxOpenSeparateWindow.Visibility = (bool)RadioButtonWindows.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            if ((bool)RadioButtonWindows.IsChecked)
            {
                LoadDefaultWindowsConfiguration();
            }
            else
            {
                // Target Linux
                ClearView();
                TabItemLinuxTerminal.Visibility = Visibility.Visible;
                //TabItemSettings.Visibility = Visibility.Visible;
                ButtonStartLog.Visibility = Visibility.Visible;

                ButtonImportAllConfiguration.IsEnabled = false;
                ButtonExportAllConfiguration.IsEnabled = false;
            }
        }

        public void LoadDefaultWindowsConfiguration()
        {
            // Target Windows
            ClearView();
            TabItemLinuxTerminal.Visibility = Visibility.Collapsed;
            //TabItemSettings.Visibility = Visibility.Collapsed;
            ButtonStartLog.Visibility = Visibility.Collapsed;

            ButtonImportAllConfiguration.IsEnabled = true;
            ButtonExportAllConfiguration.IsEnabled = true;

            readConfig(Directory.GetCurrentDirectory() + "\\PythonEngine\\config.json");
        }

        public void ClearView()
        {
            DataGridHeadTcp.ItemsSource = null;
            DataGridHeadRtu.ItemsSource = null;
            ListBoxProfiliTcp.Items.Clear();
            ListBoxProfiliRtu.Items.Clear();
            ListBoxProfiliDisponibili.Items.Clear();

            DataGridCoils.ItemsSource = null;
            DataGridInputs.ItemsSource = null;
            DataGridInputRegisters.ItemsSource = null;
            DataGridHoldingRegisters.ItemsSource = null;
        }

        public void PythonHandler()
        {
            String pythonLocation = "python";
            String command = "-u modbusServer.py";

            bool separateWindow = false;

            CheckBoxOpenSeparateWindow.Dispatcher.Invoke((Action)delegate
            {
                separateWindow = (bool)CheckBoxOpenSeparateWindow.IsChecked;
            });

            if (separateWindow)
            {

                python_gw = new Process();
                python_gw.StartInfo = new ProcessStartInfo()
                {
                    FileName = pythonLocation,
                    Arguments = command,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory() + "\\PythonEngine\\"
                };
                python_gw.EnableRaisingEvents = true;
                python_gw.Start();
                python_gw.PriorityClass = ProcessPriorityClass.High;    // Priorità massima (max 256)
            }
            else
            {
                ButtonStartWindowsThread.Dispatcher.Invoke((Action)delegate
                {
                    ButtonStartWindowsThread.IsEnabled = false;
                    ButtonStopWindowsThread.IsEnabled = true;
                    ButtonRestartWindowsThread.IsEnabled = true;
                });

                python_gw = new Process();
                python_gw.StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    FileName = pythonLocation,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory() + "\\PythonEngine\\"
                };
                python_gw.EnableRaisingEvents = true;
                python_gw.OutputDataReceived += Process_OutputDataReceived;
                python_gw.Start();
                python_gw.PriorityClass = ProcessPriorityClass.High;    // Priorità massima (max 256)

                python_gw.BeginErrorReadLine();
                python_gw.BeginOutputReadLine();

                while (modbusThreadRunning) {
                    ;
                }

                python_gw.StandardInput.WriteLine("exit");
                python_gw.StandardInput.Close();

                python_gw.WaitForExit();

                ButtonStartWindowsThread.Dispatcher.Invoke((Action)delegate
                {
                    ButtonStartWindowsThread.IsEnabled = true;
                    ButtonStopWindowsThread.IsEnabled = false;
                    ButtonRestartWindowsThread.IsEnabled = false;

                    RadioButtonWindows.IsEnabled = true;
                    RadioButtonRaspberry.IsEnabled = true;
                });
            }
            return;
        }

        public void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine("{0,-10}  [{1,-6}]:  {2}", DateTime.Now.ToString().Split(' ')[1], "python", e.Data);
            
            BufferLog.Enqueue(e.Data);
        }

        public void DequeueLog()
        {
            string log = "";
            string output = "";

            bool dequeued = true;

            while (true)
            {
                int i = 0;

                while (i < 10)
                {
                    if (BufferLog.TryDequeue(out output))
                    {
                        if (output != null)
                        {
                            log += output;

                            if (output.IndexOf("\n") == -1)
                            {
                                log += "\n";
                            }

                            i += 1;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (log.Length > 0)
                {
                    if (!disableRichTextBoxesLog)
                    {
                        RichTextBoxLog.Dispatcher.Invoke((Action)delegate
                        {
                            while (RichTextBoxLog.Document.Blocks.Count() > rowMaxLog)
                            {
                                RichTextBoxLog.Document.Blocks.Remove(RichTextBoxLog.Document.Blocks.FirstBlock); //First line
                            }

                            var p = new Paragraph();
                            p.Inlines.Add(log.Substring(0, log.Length - 1));
                            RichTextBoxLog.Document.Blocks.Add(p);

                            log = "";
                        });
                    }

                    dequeued = false;
                }


                if (!dequeued)
                {
                    dequeued = true;
                    if (!disableRichTextBoxesLog)
                    {
                        RichTextBoxLog.Dispatcher.Invoke((Action)delegate
                        {
                            if (!disableAutoscroll)
                            {
                                RichTextBoxLog.ScrollToEnd();
                            }
                        });
                    }
                }

                Thread.Sleep(10);
            }
        }

        private void ButtonStartWindowsThread_Click(object sender, RoutedEventArgs e)
        {
            // Salvo comunque la configurazione quando qualcuno fa start
            ButtonSaveWindows_Click(sender, e);

            // Cancello l'output di log
            ButtonClearTerminal_Click(null, null);

            // Lancio lo script
            currentMode = 1;

            modbusThread = new Thread(new ThreadStart(PythonHandler));
            modbusThread.IsBackground = true;
            //modbusThread.Priority = ThreadPriority.AboveNormal;
            modbusThread.Start();

            modbusThreadRunning = true;

            RadioButtonWindows.IsEnabled = false;
            RadioButtonRaspberry.IsEnabled = false;

            if (!(bool)CheckBoxOpenSeparateWindow.IsChecked)
            {
                if (TabControlMain.SelectedIndex == 0)
                {
                    TabControlMain.SelectedIndex = 2;
                }
            }
        }

        private void ButtonStopWindowsThread_Click(object sender, RoutedEventArgs e)
        {
            modbusThreadRunning = false;            
        }

        private void ButtonReadLinux_Click(object sender, RoutedEventArgs e)
        {
            Ping pingSender = new Ping();
            PingReply RispostaPING;

            RispostaPING = pingSender.Send(TextBoxAddress.Text, pingTimeout);

            if (RispostaPING.Status == IPStatus.Success)
            {
                try
                {
                    string[] filesToDownload = {
                    "/etc/ModBusServer/config.json"
                };

                    using (ScpClient scpClient = new ScpClient(TextBoxAddress.Text, int.Parse(TextBoxPort.Text), TextBoxUsername.Text, TextBoxPassword.Text))
                    {
                        scpClient.KeepAliveInterval = TimeSpan.FromSeconds(60);
                        scpClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(180);
                        scpClient.OperationTimeout = TimeSpan.FromMinutes(180);
                        scpClient.Connect();
                        bool connected = scpClient.IsConnected;

                        if (!Directory.Exists(Directory.GetCurrentDirectory() + "\\tmp"))
                        {
                            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\tmp");

                            if (!Directory.Exists(Directory.GetCurrentDirectory() + "\\tmp\\read"))
                            {
                                Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\tmp\\read");
                            }
                        }
                        else
                        {
                            string[] files;

                            if (!Directory.Exists(Directory.GetCurrentDirectory() + "\\tmp\\read"))
                            {
                                Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\tmp\\read");
                            }
                            else
                            {
                                // Cancello i file tmp
                                files = Directory.GetFiles(Directory.GetCurrentDirectory() + "\\tmp\\read");

                                foreach (string file_ in files)
                                {
                                    File.Delete(file_);
                                    Console.WriteLine("Rimosso: " + file_);
                                }
                            }

                            if (!Directory.Exists(Directory.GetCurrentDirectory() + "\\tmp\\write"))
                            {
                                Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\tmp\\write");
                            }
                            else
                            {
                                files = Directory.GetFiles(Directory.GetCurrentDirectory() + "\\tmp\\write");

                                foreach (string file_ in files)
                                {
                                    File.Delete(file_);
                                    Console.WriteLine("Rimosso: " + file_);
                                }
                            }
                        }

                        // RunCommand(host, user, password, "sudo chmod 777 -R " + remotePath);
                        foreach (string file_ in filesToDownload)
                        {
                            var file = File.OpenWrite(Directory.GetCurrentDirectory() + "\\tmp\\read\\" + System.IO.Path.GetFileName(file_));
                            scpClient.Download(file_, file);
                            file.Close();
                        }

                        scpClient.Disconnect();

                        readConfig(Directory.GetCurrentDirectory() + "\\tmp\\read\\config.json");

                        ButtonWriteLinux.IsEnabled = true;
                    }
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore read configurazione", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Console.WriteLine(err);
                }
            }
            else
            {
                MessageBox.Show("Errore connessione Raspberry, Ping fallito.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonWriteLinux_Click(object sender, RoutedEventArgs e)
        {
            writeConfig(Directory.GetCurrentDirectory() + "\\tmp\\write\\config.json");

            Ping pingSender = new Ping();
            PingReply RispostaPING;

            RispostaPING = pingSender.Send(TextBoxAddress.Text, pingTimeout);

            if (RispostaPING.Status == IPStatus.Success)
            {
                try
                {
                    string[] filesToUpload = { "/etc/ModBusServer/config.json" };

                    using (ScpClient scpClient = new ScpClient(TextBoxAddress.Text, int.Parse(TextBoxPort.Text), TextBoxUsername.Text, TextBoxPassword.Text))
                    {
                        scpClient.KeepAliveInterval = TimeSpan.FromSeconds(60);
                        scpClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(180);
                        scpClient.OperationTimeout = TimeSpan.FromMinutes(180);
                        scpClient.Connect();
                        bool connected = scpClient.IsConnected;

                        // RunCommand(host, user, password, "sudo chmod 777 -R " + remotePath);
                        foreach (string file_ in filesToUpload)
                        {
                            Stream fileRead = File.OpenRead(Directory.GetCurrentDirectory() + "\\tmp\\write\\" + System.IO.Path.GetFileName(file_));

                            scpClient.Upload(fileRead, file_);
                            fileRead.Close();
                        }

                        scpClient.Disconnect();

                    }

                    if (MessageBox.Show("Upload completato. Riavviare il servizio?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        ButtonRestartService_Click(sender, e);
                    }
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore write configurazione", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Console.WriteLine(err);
                }
            }
            else
            {
                MessageBox.Show("Errore connessione Raspberry, Ping fallito.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonStartService_Click(object sender, RoutedEventArgs e)
        {
            Thread t = new Thread(new ThreadStart(StartService));
            t.Start();
        }

        public void StartService()
        {
            string ip = "";

            TextBoxAddress.Dispatcher.Invoke((Action)delegate {
                ip = TextBoxAddress.Text;
            });

            Ping pingSender = new Ping();
            PingReply RispostaPING;

            RispostaPING = pingSender.Send(ip, pingTimeout);

            if (RispostaPING.Status == IPStatus.Success)
            {

                ButtonStartService.Dispatcher.Invoke((Action)delegate
                {
                    ButtonStartService.IsEnabled = false;
                    ButtonRestartService.IsEnabled = false;
                });

                try
                {
                    var cmd = sshClient.CreateCommand("sudo service modbus start");

                    //Console.WriteLine("Command > " + cmd.CommandText);

                    string output = cmd.Execute();

                    //Console.WriteLine(output);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        RichTextBoxOutput.AppendText("\n");
                        RichTextBoxOutput.AppendText("Command > " + cmd.CommandText + "\n");
                        RichTextBoxOutput.AppendText(output);
                        RichTextBoxOutput.ScrollToEnd();
                    });

                    //Console.WriteLine("Return Value = {0}", cmd.ExitStatus);

                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStopService.IsEnabled = true;
                        ButtonRestartService.IsEnabled = true;
                    });
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore start servizio", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStartService.IsEnabled = true;
                        ButtonStopService.IsEnabled = false;
                        ButtonRestartService.IsEnabled = true;

                        RichTextBoxOutput.AppendText(err.ToString());
                        RichTextBoxOutput.ScrollToEnd();
                    });
                }
            }
            else
            {
                MessageBox.Show("Errore connessione Raspberry, Ping fallito.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonStopService_Click(object sender, RoutedEventArgs e)
        {
            Thread t = new Thread(new ThreadStart(StopService));
            t.Start();
        }

        public void StopService()
        {
            string ip = "";

            TextBoxAddress.Dispatcher.Invoke((Action)delegate {
                ip = TextBoxAddress.Text;
            });

            Ping pingSender = new Ping();
            PingReply RispostaPING;

            RispostaPING = pingSender.Send(ip, pingTimeout);

            if (RispostaPING.Status == IPStatus.Success)
            {
                try
                {
                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStopService.IsEnabled = false;
                        ButtonRestartService.IsEnabled = false;
                    });

                    var cmd = sshClient.CreateCommand("sudo service modbus stop");

                    //Console.WriteLine("Command > " + cmd.CommandText);

                    string output = cmd.Execute();

                    //Console.WriteLine(output);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        RichTextBoxOutput.AppendText("\n");
                        RichTextBoxOutput.AppendText("Command > " + cmd.CommandText + "\n");
                        RichTextBoxOutput.AppendText(output);
                        RichTextBoxOutput.ScrollToEnd();
                    });

                    //Console.WriteLine("Return Value = {0}", cmd.ExitStatus);

                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStartService.IsEnabled = true;
                        ButtonRestartService.IsEnabled = true;
                    });
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore stop servizio", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStartService.IsEnabled = false;
                        ButtonStopService.IsEnabled = true;
                        ButtonRestartService.IsEnabled = true;

                        RichTextBoxOutput.AppendText(err.ToString());
                        RichTextBoxOutput.ScrollToEnd();
                    });
                }
            }
            else
            {
                MessageBox.Show("Errore connessione Raspberry, Ping fallito.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonRestartService_Click(object sender, RoutedEventArgs e)
        {
            Thread t = new Thread(new ThreadStart(RestartService));
            t.Start();
        }

        public void RestartService()
        {
            string ip = "";

            TextBoxAddress.Dispatcher.Invoke((Action)delegate {
                ip = TextBoxAddress.Text;
            });

            Ping pingSender = new Ping();
            PingReply RispostaPING;

            RispostaPING = pingSender.Send(ip, pingTimeout);

            if (RispostaPING.Status == IPStatus.Success)
            {
                try
                {
                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStartService.IsEnabled = false;
                        ButtonStopService.IsEnabled = false;
                        ButtonRestartService.IsEnabled = false;
                    });

                    // Pulisco la richtextbox
                    ButtonClearTerminal_Click(null, null);

                    var cmd = sshClient.CreateCommand("sudo service modbus restart");

                    //Console.WriteLine("Command > " + cmd.CommandText);

                    string output = cmd.Execute();

                    //Console.WriteLine(output);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        RichTextBoxOutput.AppendText("\n");
                        RichTextBoxOutput.AppendText("Command > " + cmd.CommandText + "\n");
                        RichTextBoxOutput.AppendText(output);
                        RichTextBoxOutput.ScrollToEnd();
                    });

                    //Console.WriteLine("Return Value = {0}", cmd.ExitStatus);

                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStopService.IsEnabled = true;
                        ButtonRestartService.IsEnabled = true;
                    });
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore restart servizio", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonStartService.IsEnabled = false;
                        ButtonStopService.IsEnabled = true;
                        ButtonRestartService.IsEnabled = true;

                        RichTextBoxOutput.AppendText(err.ToString());
                        RichTextBoxOutput.ScrollToEnd();
                    });
                }
            }
            else
            {
                MessageBox.Show("Errore connessione Raspberry, Ping fallito.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonClearTerminal_Click(object sender, RoutedEventArgs e)
        {
            lock (RichTextBoxLog)
            {
                RichTextBoxLog.Dispatcher.Invoke((Action)delegate
                {
                    RichTextBoxLog.Document.Blocks.Clear();
                    RichTextBoxLog.AppendText("\n");
                });
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            modbusThreadRunning = false;

            if ((bool)RadioButtonWindows.IsChecked)
            {
                // Salvo file configurazione ModBus
                writeConfig(Directory.GetCurrentDirectory() + "\\PythonEngine\\config.json");
            }
            else
            {
                // Se ero connesso forzo la disconnessione e chiedo all'utente se salvare la configurazione
                if (connected)
                {
                    ConnectDisconnect();
                }
            }

            // Salvo configurazione client
            saveConfig();

            try
            {
                if (logDequeueThread != null)
                {
                    logDequeueThread.Abort();
                }
            }
            catch { }

        }

        private void ButtonSaveWindows_Click(object sender, RoutedEventArgs e)
        {
            writeConfig(Directory.GetCurrentDirectory() + "\\PythonEngine\\config.json");
        }

        private void ListBoxProfiliTcp_DragEnter(object sender, DragEventArgs e)
        {
            if (DataGridHeadTcp.SelectedIndex >= 0)
            {
                if (!ListBoxProfiliTcp.Items.Contains(e.Data.GetData(DataFormats.Text)))
                {
                    if (DataGridHeadTcp.SelectedIndex < ActualConfig.TCP.Count)
                    {
                        // Se l'oogetto è stato creato nella datagrid lo istanzio
                        if (ActualConfig.TCP[DataGridHeadTcp.SelectedIndex].slaves is null)
                        {
                            ActualConfig.TCP[DataGridHeadTcp.SelectedIndex].slaves = new List<string>();
                        }

                        ListBoxProfiliTcp.Items.Add(e.Data.GetData(DataFormats.Text));
                        ActualConfig.TCP[DataGridHeadTcp.SelectedIndex].slaves.Add(e.Data.GetData(DataFormats.Text).ToString());
                    }
                }
            }
        }

        private void ListBoxProfiliRtu_DragEnter(object sender, DragEventArgs e)
        {
            if (DataGridHeadRtu.SelectedIndex >= 0)
            {
                if (!ListBoxProfiliRtu.Items.Contains(e.Data.GetData(DataFormats.Text)))
                {
                    if (DataGridHeadRtu.SelectedIndex < ActualConfig.RTU.Count)
                    {
                        if (ActualConfig.RTU[DataGridHeadRtu.SelectedIndex].slaves is null)
                        {
                            ActualConfig.RTU[DataGridHeadRtu.SelectedIndex].slaves = new List<string>();
                        }

                        ListBoxProfiliRtu.Items.Add(e.Data.GetData(DataFormats.Text));
                        ActualConfig.RTU[DataGridHeadRtu.SelectedIndex].slaves.Add(e.Data.GetData(DataFormats.Text).ToString());
                    }
                }
            }
        }

        private void ListBoxProfiliTcp_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                MenuItemRimuoviProfiloTCP_Click(sender, e);
            }
        }

        private void ListBoxProfiliRtu_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                MenuItemRimuoviProfiloRTU_Click(sender, e);
            }
        }

        private void ButtonClearTerminal_Copy_Click(object sender, RoutedEventArgs e)
        {
            RichTextBoxOutput.Document.Blocks.Clear();
            RichTextBoxOutput.AppendText("\n");
        }

        private void CheckBoxDebug_Checked(object sender, RoutedEventArgs e)
        {
            if (ActualConfig.debug != (bool)(CheckBoxDebug.IsChecked))
            {
                ActualConfig.debug = (bool)(CheckBoxDebug.IsChecked);

                if (!(bool)RadioButtonWindows.IsChecked)
                {
                    // Raspberry Mode
                    if (connected)
                    {
                        if (MessageBox.Show("Premere write per scrivere la configurazione e applicare le modifiche. Scriverla ora?", "Question", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            ButtonWriteLinux_Click(null, null);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Premere write per scrivere la configurazione e applicare le modifiche.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    // Windows Mode
                    if ((bool)CheckBoxOpenSeparateWindow.IsChecked)
                    {
                        MessageBox.Show("Chiudere la finestra del cmd e lanciare nuovamente il server con \"Start\" per applicare le modifiche.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        DoEvents();

                        if (MessageBox.Show("Premere \"Stop\" e \"Start\" per applicare le modifiche. Riavviare il server ora?", "Question", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            ButtonStopWindowsThread_Click(null, null);
                            Thread.Sleep(100);
                            DoEvents();
                            ButtonStartWindowsThread_Click(null, null);
                        }
                    }
                }
            }

            ActualConfig.debug = (bool)(CheckBoxDebug.IsChecked);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Title = "ModBus Server Configurator - " + Assembly.GetExecutingAssembly().GetName().Version;
            loadConfig();

            logDequeueThread = new Thread(new ThreadStart(DequeueLog));
            logDequeueThread.IsBackground = false;
            logDequeueThread.Priority = ThreadPriority.AboveNormal;
            logDequeueThread.Start();

            TabControlModBusMapping.SelectedIndex = 3;
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                if (e.Key == Key.C)
                {
                    if (consoleIsOpen)
                    {
                        chiudiConsole();
                    }
                    else
                    {
                        apriConsole();

                        this.Focus();
                    }
                }
            }
        }

        private void DataGridHeadTcp_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            try
            {
                ListBoxProfiliTcp.Items.Clear();

                if (DataGridHeadTcp.SelectedIndex < ActualConfig.TCP.Count())
                {
                    MOD_HeadTcp currentHead = (MOD_HeadTcp)(DataGridHeadTcp.SelectedItem);

                    if (currentHead != null)
                    {
                        if (currentHead.slaves != null)
                        {
                            foreach (string profile in currentHead.slaves)
                            {
                                ListBoxProfiliTcp.Items.Add(profile);
                            }
                        }

                        ListBoxProfiliTcp.Background = Brushes.White;
                    }
                    else
                    {
                        BrushConverter bc = new BrushConverter();
                        ListBoxProfiliTcp.Background = Brushes.White;
                    }
                }
                else
                {
                    BrushConverter bc = new BrushConverter();
                    ListBoxProfiliTcp.Background = (Brush)bc.ConvertFrom("#FFE5E5E5");
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }

            //debug
            //Console.WriteLine("DataGridHeadTcp_SelectedCellsChanged");
        }

        public void AggiornaProfiliListAndComboBox()
        {
            try
            {
                ComboBoxProfiles.Items.Clear();
                ListBoxProfiliDisponibili.Items.Clear();

                foreach (MOD_SlaveProfile slave in ActualConfig.profiles)
                {
                    ComboBoxProfiles.Items.Add(slave.label);
                    ListBoxProfiliDisponibili.Items.Add(slave.label);
                }

                ListBoxProfiliDisponibili.SelectedIndex = 0;
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void DataGridHeadRtu_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            try
            {
                ListBoxProfiliRtu.Items.Clear();

                if (DataGridHeadRtu.SelectedIndex < ActualConfig.RTU.Count())
                {
                    MOD_HeadRtu currentHead = (MOD_HeadRtu)(DataGridHeadRtu.SelectedItem);

                    if (currentHead != null)
                    {
                        if (currentHead.slaves != null)
                        {
                            foreach (string profile in currentHead.slaves)
                            {
                                ListBoxProfiliRtu.Items.Add(profile);
                            }
                        }

                        ListBoxProfiliRtu.Background = Brushes.White;
                    }
                    else
                    {
                        BrushConverter bc = new BrushConverter();
                        ListBoxProfiliRtu.Background = Brushes.White;
                    }
                }
                else
                {
                    BrushConverter bc = new BrushConverter();
                    ListBoxProfiliRtu.Background = (Brush)bc.ConvertFrom("#FFE5E5E5");
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void ButtonConnectLinux_Click(object sender, RoutedEventArgs e)
        {
            Thread t = new Thread(new ThreadStart(ConnectDisconnect));
            t.Start();
        }

        public void ConnectDisconnect()
        {
            if (!connected)
            {
                try
                {
                    Ping pingSender = new Ping();
                    PingReply RispostaPING;

                    string ip = "";

                    TextBoxAddress.Dispatcher.Invoke((Action)delegate
                    {
                         ip = TextBoxAddress.Text;
                    });

                    RispostaPING = pingSender.Send(ip, pingTimeout);

                    if (RispostaPING.Status != IPStatus.Success)
                    {
                        Console.WriteLine("Ping to " + ip + " failed");
                        throw new Exception();
                    }
                    
                    TextBoxAddress.Dispatcher.Invoke((Action)delegate
                    {

                        ButtonConnectLinux.IsEnabled = false;
                        sshClient = new SshClient(TextBoxAddress.Text, int.Parse(TextBoxPort.Text), TextBoxUsername.Text, TextBoxPassword.Text);
                    });

                    sshClient.Connect();

                    // Controllo lo stato del servizio
                    var cmd = sshClient.CreateCommand("sudo service modbus status");
                    string output = cmd.Execute();

                    RichTextBoxOutput.Dispatcher.Invoke((Action)delegate
                    {
                        RichTextBoxOutput.AppendText("\n");
                        RichTextBoxOutput.AppendText("Command > " + cmd.CommandText + "\n");
                        RichTextBoxOutput.AppendText(output);
                        RichTextBoxOutput.ScrollToEnd();
                    });

                    if (output.IndexOf("Active: active (running) since") != -1)
                    {
                        ButtonStartService.Dispatcher.Invoke((Action)delegate
                        {
                            ButtonStartService.IsEnabled = false;
                            ButtonStopService.IsEnabled = true;
                            ButtonRestartService.IsEnabled = true;
                        });
                    }
                    else if(output.IndexOf("Active: inactive (dead)") != -1)
                    {
                        ButtonStartService.Dispatcher.Invoke((Action)delegate
                        {
                            ButtonStartService.IsEnabled = true;
                            ButtonStopService.IsEnabled = false;
                            ButtonRestartService.IsEnabled = true;
                        });
                    }
                    else
                    {
                        ButtonStartService.Dispatcher.Invoke((Action)delegate
                        {
                            ButtonStartService.IsEnabled = false;
                            ButtonStopService.IsEnabled = false;
                            ButtonRestartService.IsEnabled = false;
                        });

                        MessageBoxResult Result =  MessageBox.Show("Il servizio ModBus non è installato sul Raspberry target. Procedere con l'installazione?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        String address = "";
                        String port = "";
                        String user = "";
                        String pass = "";

                        this.Dispatcher.Invoke((Action)delegate
                        {
                            address = TextBoxAddress.Text;
                            port = TextBoxPort.Text;
                            user = TextBoxUsername.Text;
                            pass = TextBoxPassword.Text;
                        });

                        if (Result == MessageBoxResult.Yes)
                        {
                            TabControlMain.Dispatcher.Invoke((Action)delegate
                            {
                                TabControlMain.SelectedIndex = 2;
                            });

                            BufferLog.Enqueue("");
                            BufferLog.Enqueue("Avvio installazione:");

                            // Carico installer
                            string[] filesToUpload = { "/home/" + user + "/dnsmasq.conf", "/home/" + user + "/hostapd.conf", "/home/" + user + "/installer.sh", "/home/" + user + "/wlan0" };

                            using (ScpClient scpClient = new ScpClient(address, int.Parse(port), user, pass))
                            {
                                scpClient.KeepAliveInterval = TimeSpan.FromSeconds(60);
                                scpClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(180);
                                scpClient.OperationTimeout = TimeSpan.FromMinutes(180);
                                scpClient.Connect();
                                bool connected = scpClient.IsConnected;

                                // RunCommand(host, user, password, "sudo chmod 777 -R " + remotePath);
                                foreach (string file_ in filesToUpload)
                                {
                                    Stream fileRead = File.OpenRead(Directory.GetCurrentDirectory() + "\\LinuxInstaller\\" + System.IO.Path.GetFileName(file_));

                                    scpClient.Upload(fileRead, file_);
                                    fileRead.Close();
                                }

                                scpClient.Disconnect();
                            }

                            // Creo cartella di programma
                            cmd = sshClient.CreateCommand("mkdir -p /home/" + user + "/ModBusServer");

                            BufferLog.Enqueue(cmd.Execute());

                            string[] filesToUpload_2 = { "/home/" + user + "/ModBusServer/config.json", "/home/" + user + "/ModBusServer/modbus.service", "/home/" + user + "/ModBusServer/modbusServer.py" };

                            using (ScpClient scpClient = new ScpClient(address, int.Parse(port), user, pass))
                            {
                                scpClient.KeepAliveInterval = TimeSpan.FromSeconds(60);
                                scpClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(180);
                                scpClient.OperationTimeout = TimeSpan.FromMinutes(180);
                                scpClient.Connect();
                                bool connected = scpClient.IsConnected;

                                // RunCommand(host, user, password, "sudo chmod 777 -R " + remotePath);
                                foreach (string file_ in filesToUpload_2)
                                {
                                    Stream fileRead = File.OpenRead(Directory.GetCurrentDirectory() + "\\PythonEngine\\" + System.IO.Path.GetFileName(file_));

                                    scpClient.Upload(fileRead, file_);
                                    fileRead.Close();
                                }

                                scpClient.Disconnect();
                            }

                            // CRLF to LF
                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /home/" + user + "/dnsmasq.conf");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /home/" + user + "/hostapd.conf");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /home/" + user + "/installer.sh");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/$USER/" + user + "/g' /home/" + user + "/installer.sh");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /home/" + user + "/wlan0");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /etc/ModBusServer/config.json");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /etc/ModBusServer/modbus.service");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("sed -i 's/\r$//g' /etc/ModBusServer/modbusServer.py");
                            BufferLog.Enqueue(cmd.Execute());

                            // Controllo lo stato del servizio
                            cmd = sshClient.CreateCommand("chmod +x /home/" + user + "/installer.sh");
                            BufferLog.Enqueue(cmd.Execute());

                            cmd = sshClient.CreateCommand("/home/" + user + "/installer.sh");
                            var result = cmd.BeginExecute();

                            uint count = 0;
                            long timer = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                            using (var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8, true, 1024, true))
                            {
                                while (!result.IsCompleted || !reader.EndOfStream)
                                {
                                    string line = reader.ReadLine();
                                    if (line != null)
                                    {
                                        line = line.Replace("\n", "");
                                        BufferLog.Enqueue(line);
                                        count += 1;
                                        Console.WriteLine("{0,-10}  [{1,-6}]:  {2}", DateTime.Now.ToString().Split(' ')[1], "raspberry", line);

                                        // Debug
                                        //Console.WriteLine("count: " + count.ToString());

                                        if ((DateTimeOffset.Now.ToUnixTimeMilliseconds() - timer) > 1000)
                                        {
                                            // debug
                                            //Console.WriteLine("rows per second: " + count.ToString());

                                            count = 0;
                                            timer = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        }
                                    }
                                }
                            }

                            cmd.EndExecute(result);

                            // Controllo lo stato del servizio
                            cmd = sshClient.CreateCommand("sudo service modbus status");
                            output = cmd.Execute();

                            if (output.IndexOf("Active: active (running) since") != -1)
                            {
                                ButtonStartService.Dispatcher.Invoke((Action)delegate
                                {
                                    ButtonStartService.IsEnabled = false;
                                    ButtonStopService.IsEnabled = true;
                                    ButtonRestartService.IsEnabled = true;
                                });
                            }
                            else if (output.IndexOf("Active: inactive (dead)") != -1)
                            {
                                ButtonStartService.Dispatcher.Invoke((Action)delegate
                                {
                                    ButtonStartService.IsEnabled = true;
                                    ButtonStopService.IsEnabled = false;
                                    ButtonRestartService.IsEnabled = true;
                                });
                            }
                        }
                    }
                    
                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        if ((bool)CheckBoxSubscribeLog.IsChecked)
                        {
                            ButtonClearTerminal_Click(null, null);
                            ButtonStartLog_Click(null, null);
                        }
                    });

                    currentMode = 2;
                    
                    ButtonStartService.Dispatcher.Invoke((Action)delegate
                    {
                        TextBlockConnectLinux.Text = "Disconnect";
                        RadioButtonWindows.IsEnabled = false;
                        RadioButtonRaspberry.IsEnabled = false;
                        ButtonConnectLinux.IsEnabled = true;
                        connected = true;

                        ButtonReadLinux_Click(null, null);
                    });
                }
                catch (Exception err)
                {
                    MessageBox.Show("Errore connessione Raspberry", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Console.WriteLine(err);

                    ButtonConnectLinux.Dispatcher.Invoke((Action)delegate
                    {
                        ButtonConnectLinux.IsEnabled = true;
                    });
                }
            }
            else
            {
                if (MessageBox.Show("Scrivere la configurazione attuale prima di disconnetttersi?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ButtonWriteLinux_Click(null, null);
                }

                ButtonStartService.Dispatcher.Invoke((Action)delegate
                {
                    TextBlockConnectLinux.Text = "Connect";
                    RadioButtonWindows.IsEnabled = true;
                    RadioButtonRaspberry.IsEnabled = true;
                    ButtonConnectLinux.IsEnabled = false;
                    connected = false;
                });

                closeTailLog();

                try
                {
                    sshClient.Disconnect();
                }
                catch (Exception err)
                {

                }

                ButtonStartService.Dispatcher.Invoke((Action)delegate
                {
                    ButtonWriteLinux.IsEnabled = false;

                    ButtonStartService.IsEnabled = false;
                    ButtonStopService.IsEnabled = false;
                    ButtonRestartService.IsEnabled = false;
                });
            }

            ButtonStartService.Dispatcher.Invoke((Action)delegate
            {
                ButtonConnectLinux.IsEnabled = true;
                ButtonReadLinux.IsEnabled = connected;
                //ButtonStartService.IsEnabled = connected;
                //ButtonStopService.IsEnabled = connected;
                //ButtonRestartService.IsEnabled = connected;

                TextBoxAddress.IsEnabled = !connected;
                TextBoxPort.IsEnabled = !connected;
                TextBoxUsername.IsEnabled = !connected;
                TextBoxPassword.IsEnabled = !connected;

                ButtonPowerOff.IsEnabled = connected;
                ButtonSendManualCommand.IsEnabled = connected;

                ButtonDownloadFromRaspberry.IsEnabled = connected;
                ButtonUploadToRasperry.IsEnabled = connected;
                ButtonPowerOffRaspberryConsole.IsEnabled = connected;

                ButtonImportAllConfiguration.IsEnabled = connected;
                ButtonExportAllConfiguration.IsEnabled = connected;
            });
        }

        public void RaspberryEnqueue()
        {
            var cmd = sshClient.CreateCommand("tail -f /var/log/modbus.log");

            var result = cmd.BeginExecute();

            uint count = 0;
            long timer = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            using (var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8, true, 1024, true))
            {
                while (!result.IsCompleted || !reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line != null)
                    {
                        line = line.Replace("\n", "");
                        BufferLog.Enqueue(line);
                        count += 1;
                        Console.WriteLine("{0,-10}  [{1,-6}]:  {2}", DateTime.Now.ToString().Split(' ')[1], "raspberry", line);

                        // Debug
                        //Console.WriteLine("count: " + count.ToString());

                        if((DateTimeOffset.Now.ToUnixTimeMilliseconds() - timer) > 1000)
                        {
                            // debug
                            //Console.WriteLine("rows per second: " + count.ToString());

                            count = 0;
                            timer = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        }
                    }
                }
            }

            cmd.EndExecute(result);
            modbusThreadRunning = false;
        }

        public class FixedSizedQueue<T> : ConcurrentQueue<T>
        {
            private readonly object syncObject = new object();

            public int Size { get; private set; }

            public FixedSizedQueue(int size)
            {
                Size = size;
            }

            public new void Enqueue(T obj)
            {
                base.Enqueue(obj);
                lock (syncObject)
                {
                    while (base.Count > Size)
                    {
                        T outObj;
                        base.TryDequeue(out outObj);
                    }
                }
            }
        }


        public class MOD_Config
        {
            public bool debug { get; set; }             // Modalità debug server modebus
            public bool assignIp { get; set; }          // True su raspberry assegna automaticamente indirizzi ip
            public List<MOD_HeadTcp> TCP { get; set; }

            public List<MOD_HeadRtu> RTU { get; set; }

            public List<MOD_SlaveProfile> profiles { get; set; }
        }

        public class MOD_HeadTcp
        {
            public bool enable { get; set; }
            public string label { get; set; }
            public string notes { get; set; }
            public string ip_address { get; set; }
            public int port { get; set; }
            public List<string> slaves { get; set; }
        }

        public class MOD_HeadRtu
        {
            public bool enable { get; set; }
            public string label { get; set; }
            public string notes { get; set; }
            public string serial { get; set; }
            public int baudrate { get; set; }
            public string config { get; set; }
            public List<string> slaves { get; set; }
        }

        public class MOD_SlaveProfile
        {
            public List<int> slave_id { get; set; }
            public string label { get; set; }
            public string notes { get; set; }
            public string type { get; set; }

            public MOD_RegProfile di { get; set; }
            public MOD_RegProfile co { get; set; }
            public MOD_RegProfile ir { get; set; }
            public MOD_RegProfile hr { get; set; }
        }
        public class MOD_RegProfile
        {
            public int len { get; set; }
            public List<MOD_Reg> data { get; set; }
        }

        public class MOD_Reg
        {
            public string reg { get; set; }
            public Int32 value { get; set; }
            public string label { get; set; }
        }

        private void CheckBoxDisableRichTextBox_Checked(object sender, RoutedEventArgs e)
        {
            disableRichTextBoxesLog = (bool)CheckBoxDisableRichTextBox.IsChecked;
            RichTextBoxLog.IsEnabled = !(bool)CheckBoxDisableRichTextBox.IsChecked;
        }

        private void CheckBoxDisableAutoscroll_Checked(object sender, RoutedEventArgs e)
        {
            disableAutoscroll = (bool)CheckBoxDisableAutoscroll.IsChecked;
        }

        private void ButtonNewProfile_Click(object sender, RoutedEventArgs e)
        {
            NewProfile();
        }

        private void TextBoxCoilsLen_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string profile = ComboBoxProfiles.SelectedItem.ToString();

                //Console.WriteLine("Selcted item: " + profile);

                try
                {
                    MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                    if (slave != null)
                    {
                        int tmp = 0;

                        if (int.TryParse(TextBoxCoilsLen.Text, out tmp))
                        {
                            slave.co.len = tmp;
                        }
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err);
                }
            }
            catch { }
        }

        private void TextBoxInputsLen_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string profile = ComboBoxProfiles.SelectedItem.ToString();

                //debug
                //Console.WriteLine("Selcted item: " + profile);

                try
                {
                    MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                    if (slave != null)
                    {
                        int tmp = 0;

                        if (int.TryParse(TextBoxInputsLen.Text, out tmp))
                        {
                            slave.di.len = tmp;
                        }
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err);
                }
            }
            catch { }
        }

        private void TextBoxInputRegisterLen_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string profile = ComboBoxProfiles.SelectedItem.ToString();

                //debug
                //Console.WriteLine("Selcted item: " + profile);

                try
                {
                    MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                    if (slave != null)
                    {
                        int tmp = 0;

                        if (int.TryParse(TextBoxInputRegisterLen.Text, out tmp))
                        {
                            slave.ir.len = tmp;
                        }
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err);
                }
            }
            catch { }
        }

        private void TextBoxHoldingRegisterLen_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string profile = ComboBoxProfiles.SelectedItem.ToString();

                //debug
                //Console.WriteLine("Selcted item: " + profile);

                try
                {
                    MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                    if (slave != null)
                    {
                        int tmp = 0;

                        if (int.TryParse(TextBoxHoldingRegisterLen.Text, out tmp))
                        {
                            slave.hr.len = tmp;
                        }
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err);
                }
            }
            catch { }
        }

        private void TextBoxDeviceId_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string profile = ComboBoxProfiles.SelectedItem.ToString();

                //debug
                //Console.WriteLine("Selcted item: " + profile);

                try
                {
                    MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profile);

                    if (slave != null)
                    {
                        int tmp = 0;

                        List<int> tmp1 = new List<int>();

                        foreach(string tmp2 in TextBoxDeviceId.Text.Split(','))
                        {
                            tmp1.Add(int.Parse(tmp2));
                        }

                        slave.slave_id = new List<int>(tmp1);
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err);
                }
            }
            catch { }
        }

        private void ButtonStartLog_Click(object sender, RoutedEventArgs e)
        {
            if (modbusThreadRunning)
            {
                closeTailLog();
            }
            else
            {
                modbusThread = new Thread(new ThreadStart(RaspberryEnqueue));
                modbusThread.IsBackground = false;
                modbusThread.Priority = ThreadPriority.AboveNormal;
                modbusThread.Start();

                ButtonStartLog.Content = "Stop Log";
                modbusThreadRunning = true;
            }
        }

        public void closeTailLog()
        {
            try
            {
                modbusThread.Abort();
            }
            catch { }

            ButtonStartLog.Dispatcher.Invoke((Action)delegate
            {
                ButtonStartLog.Content = "Start Log";
                modbusThreadRunning = false;
            });
        }

        private void MenuModificaProfiloList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string profile = ListBoxProfiliDisponibili.SelectedItem.ToString();

                TabControlMain.SelectedIndex = 1;


                for (int i = 0; i < ComboBoxProfiles.Items.Count; i++)
                {
                    if (ComboBoxProfiles.Items[i].ToString() == profile)
                    {
                        ComboBoxProfiles.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void MenuItemModificaProfiloTCP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ListBoxProfiliTcp.SelectedItem != null)
                {
                    string profile = ListBoxProfiliTcp.SelectedItem.ToString();

                    TabControlMain.SelectedIndex = 1;


                    for (int i = 0; i < ComboBoxProfiles.Items.Count; i++)
                    {
                        if (ComboBoxProfiles.Items[i].ToString() == profile)
                        {
                            ComboBoxProfiles.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void MenuItemModificaProfiloRTU_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ListBoxProfiliRtu.SelectedItem != null)
                {
                    string profile = ListBoxProfiliRtu.SelectedItem.ToString();

                    TabControlMain.SelectedIndex = 1;


                    for (int i = 0; i < ComboBoxProfiles.Items.Count; i++)
                    {
                        if (ComboBoxProfiles.Items[i].ToString() == profile)
                        {
                            ComboBoxProfiles.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void MenuItemRimuoviProfiloTCP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ActualConfig.TCP[DataGridHeadTcp.SelectedIndex].slaves.Remove(ListBoxProfiliTcp.SelectedItem.ToString());
                ListBoxProfiliTcp.Items.Remove(ListBoxProfiliTcp.SelectedItem);
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void MenuItemRimuoviProfiloRTU_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ActualConfig.RTU[DataGridHeadRtu.SelectedIndex].slaves.Remove(ListBoxProfiliRtu.SelectedItem.ToString());
                ListBoxProfiliRtu.Items.Remove(ListBoxProfiliRtu.SelectedItem);
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void ButtonDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboBoxProfiles.SelectedItem != null)
            {
                MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == ComboBoxProfiles.SelectedItem.ToString());

                if (slave != null)
                {
                    DeleteProfile(slave);
                }
            }
        }

        private void MenuModificaEliminaList_Click(object sender, RoutedEventArgs e)
        {
            if (ListBoxProfiliDisponibili.SelectedItem != null)
            {
                MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == ListBoxProfiliDisponibili.SelectedItem.ToString());

                if (slave != null)
                {
                    DeleteProfile(slave);
                }
            }
        }

        private void ListBoxProfiliDisponibili_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (ListBoxProfiliDisponibili.Items.Count == 0)
                    {
                        return;
                    }

                    string s = ListBoxProfiliDisponibili.Items[ListBoxProfiliDisponibili.SelectedIndex].ToString();
                    DragDropEffects dde1 = DragDrop.DoDragDrop(ListBoxProfiliDisponibili, s, DragDropEffects.All);

                    // Tolgo la selezione dalla listbox
                    ListBoxProfiliDisponibili.SelectedIndex = -1;

                    //debug
                    //Console.WriteLine("ListBoxProfiliDisponibili_MouseDown");
                }
            }
            catch
            {

            }
        }

        private void MenuNuovoProfiloList_Click(object sender, RoutedEventArgs e)
        {
            // Seleziono la tab profilo
            TabControlMain.SelectedIndex = 1;

            // Uso la funzione del button nuovo profilo
            NewProfile();
        }

        private void MenuRinominaProfiloList_Click(object sender, RoutedEventArgs e)
        {
            if (ListBoxProfiliDisponibili.SelectedItem != null)
            {
                RenameProfile(ActualConfig.profiles.Find(tmp => tmp.label == ListBoxProfiliDisponibili.SelectedItem.ToString()));
            }
        }

        private void MenuImportaProfiloList_Click(object sender, RoutedEventArgs e)
        {
            ImportProfile();
        }

        private void MenuEsportaProfiloList_Click(object sender, RoutedEventArgs e)
        {
            if (ListBoxProfiliDisponibili.SelectedItem != null)
            {
                ExportProfile(ListBoxProfiliDisponibili.SelectedItem.ToString());
            }
        }

        private void ButtonImportProfile_Click(object sender, RoutedEventArgs e)
        {
            ImportProfile();
        }

        private void ButtonExportProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboBoxProfiles.SelectedItem != null)
            {
                ExportProfile(ComboBoxProfiles.SelectedItem.ToString());
            }
        }

        private void ButtonRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboBoxProfiles.SelectedItem != null)
            {
                RenameProfile(ActualConfig.profiles.Find(tmp => tmp.label == ComboBoxProfiles.SelectedItem.ToString()));
            }
        }

        // ---------------------------------------------
        // -------- Funzioni specifiche profili --------
        // ---------------------------------------------
        public void NewProfile()
        {
            AddProfile window = new AddProfile();

            if ((bool)window.ShowDialog())
            {
                MOD_SlaveProfile profile = new MOD_SlaveProfile();
                profile.slave_id = new List<int>();
                profile.slave_id.Add(1);

                profile.label = window.fileName;
                profile.type = "ModBusSlave";

                profile.di = new MOD_RegProfile();
                profile.di.len = 120;
                profile.di.data = new List<MOD_Reg>();

                profile.co = new MOD_RegProfile();
                profile.co.len = 120;
                profile.co.data = new List<MOD_Reg>();

                profile.ir = new MOD_RegProfile();
                profile.ir.len = 120;
                profile.ir.data = new List<MOD_Reg>();

                profile.hr = new MOD_RegProfile();
                profile.hr.len = 120;
                profile.hr.data = new List<MOD_Reg>();

                ActualConfig.profiles.Add(profile);

                AggiornaProfiliListAndComboBox();

                ComboBoxProfiles.SelectedIndex = 0;
                ComboBoxProfiles.SelectedItem = profile.label;
            }
        }

        public void DeleteProfile(MOD_SlaveProfile slave)
        {
            try
            {
                if (MessageBox.Show("Eliminare il profilo " + slave.label + "?", "Elimina profilo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (slave != null)
                    {
                        if (ActualConfig.profiles.Remove(slave))
                        {
                            Console.WriteLine("Rimosso slave: " + slave.label);
                        }

                        // Cancello eventuali profili dai blocchi RTU e TCP
                        for (int i = 0; i < ActualConfig.RTU.Count; i++)
                        {
                            if (ActualConfig.RTU[i].slaves.Remove(slave.label))
                            {
                                Console.WriteLine("Rimosso slave: " + slave.label + " dal profilo RTU: " + ActualConfig.RTU[i].label);
                            }
                        }
                        for (int i = 0; i < ActualConfig.TCP.Count; i++)
                        {
                            if (ActualConfig.TCP[i].slaves.Remove(slave.label))
                            {
                                Console.WriteLine("Rimosso slave: " + slave.label + " dal profilo TCP: " + ActualConfig.TCP[i].label);
                            }
                        }

                        // Aggiorno lista profili disponibili
                        AggiornaProfiliListAndComboBox();

                        // Seleziono il primo profilo disponibile per non lasciare la comboBox vuota
                        ComboBoxProfiles.SelectedIndex = 0;

                        // Aggiorno lista profili TCP/RTU del profilo selezionato
                        DataGridHeadTcp_SelectedCellsChanged(null, null);
                        DataGridHeadRtu_SelectedCellsChanged(null, null);
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        public void ExportProfile(string profileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            if (Directory.Exists(TextBoxOldPathImportExportProfile.Text))
            {
                saveFileDialog.InitialDirectory = TextBoxOldPathImportExportProfile.Text;
            }
        
            saveFileDialog.Filter = "json files (*.json)|*.json";
            //saveFileDialog.FilterIndex = 2;
            saveFileDialog.RestoreDirectory = true;

            if ((bool)saveFileDialog.ShowDialog())
            {
                TextBoxOldPathImportExportProfile.Text = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);

                string profilePath = saveFileDialog.FileName;

                MOD_SlaveProfile slave = ActualConfig.profiles.Find(tmp => tmp.label == profileName);

                if (slave != null)
                {
                    JavaScriptSerializer jss = new JavaScriptSerializer();
                    File.WriteAllText(profilePath, jss.Serialize(slave));
                }
            }
        }

        public void ImportProfile()
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();

                if (Directory.Exists(TextBoxOldPathImportExportProfile.Text))
                {
                    openFileDialog.InitialDirectory = TextBoxOldPathImportExportProfile.Text;
                }

                openFileDialog.Filter = "json files (*.json)|*.json";
                //openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if ((bool)openFileDialog.ShowDialog())
                {
                    TextBoxOldPathImportExportProfile.Text = System.IO.Path.GetDirectoryName(openFileDialog.FileName);

                    JavaScriptSerializer jss = new JavaScriptSerializer();
                    dynamic profile = jss.DeserializeObject(File.ReadAllText(openFileDialog.FileName));

                    if (!profile.ContainsKey("type"))
                    {
                        // MessageBox.Show("Profile file is not a valid json for this application", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        MessageBox.Show("Il file selezionato non è un profilo json valido", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (profile["type"] != "ModBusSlave")
                    {
                        //MessageBox.Show("Profile file is not a valid json for this application", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        MessageBox.Show("Il file selezionato non è un profilo json valido", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    MOD_SlaveProfile slave = new MOD_SlaveProfile();

                    slave.label = profile["label"];
                    slave.notes = profile["notes"];

                    // Controllo che non esista gia' un profilo con l'etichetta che sto importando

                    slave.slave_id = new List<int>();

                    foreach (int slaveId in profile["slave_id"])
                    {
                        slave.slave_id.Add(slaveId);
                    }

                    string[] keys = new string[] { "di", "co", "ir", "hr" };

                    foreach (string key in keys)
                    {
                        MOD_RegProfile di = new MOD_RegProfile();
                        di.len = profile[key]["len"];
                        di.data = new List<MOD_Reg>();

                        List<MOD_Reg> list = new List<MOD_Reg>();

                        foreach (dynamic regConfig in profile[key]["data"])
                        {
                            MOD_Reg reg = new MOD_Reg();

                            reg.reg = regConfig["reg"];
                            reg.label = regConfig["label"];
                            reg.value = (UInt16)(regConfig["value"]);

                            di.data.Add(reg);
                        }

                        switch (key)
                        {
                            case "di":
                                slave.di = di;
                                break;

                            case "co":
                                slave.co = di;
                                break;

                            case "ir":
                                slave.ir = di;
                                break;

                            case "hr":
                                slave.hr = di;
                                break;
                        }
                    }

                    if (ActualConfig.profiles.Find(tmp => tmp.label == slave.label) == null)
                    {
                        ActualConfig.profiles.Add(slave);
                        AggiornaProfiliListAndComboBox();

                        try
                        {
                            // Seleziono l'ultimo
                            ComboBoxProfiles.SelectedIndex = ComboBoxProfiles.Items.Count - 1;
                        }
                        catch { }
                    }
                    else
                    {
                        bool proceed = false;

                        do
                        {
                            if (MessageBox.Show("Impossibile importare il profilo, esiste già un profilo con il nome: " + slave.label + ".\n\nImportarlo con un nuovo nome?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
                            {
                                RenameProfile window = new RenameProfile(slave.label);
                                window.Owner = this;

                                if ((bool)window.ShowDialog())
                                {

                                    slave.label = window.fileName;

                                    if (ActualConfig.profiles.Find(tmp => tmp.label == slave.label) == null)
                                    {
                                        proceed = true;

                                        ActualConfig.profiles.Add(slave);
                                        AggiornaProfiliListAndComboBox();

                                        try
                                        {
                                            // Seleziono l'ultimo
                                            ComboBoxProfiles.SelectedIndex = ComboBoxProfiles.Items.Count - 1;
                                        }
                                        catch { }
                                    }
                                }
                                else
                                {
                                    proceed = true;
                                }
                            }
                            else
                            {
                                proceed = true;
                            }
                        } while (!proceed);
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
                MessageBox.Show("Il file selezionato non è un file profilo valido.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RenameProfile(MOD_SlaveProfile slave)
        {
            if (slave != null)
            {
                RenameProfile window = new RenameProfile(slave.label);
                window.Owner = this;

                if ((bool)window.ShowDialog())
                {
                    string oldLabel = slave.label;

                    // Controllo che il nome non esista gia'
                    if (ActualConfig.profiles.Find(tmp => tmp.label == window.fileName) != null)
                    {
                        MessageBox.Show("Impossibile rinominare il profilo, esiste già un profilo con il nome: " + window.fileName, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        slave.label = window.fileName;

                        // Rinomino eventuali profili dai blocchi RTU e TCP
                        for (int i = 0; i < ActualConfig.RTU.Count; i++)
                        {
                            if (ActualConfig.RTU[i].slaves.Remove(oldLabel))
                            {
                                ActualConfig.RTU[i].slaves.Add(slave.label);
                                Console.WriteLine("Rinominato slave: " + slave.label + " con profilo RTU: " + ActualConfig.RTU[i].label);
                            }
                        }
                        for (int i = 0; i < ActualConfig.TCP.Count; i++)
                        {
                            if (ActualConfig.TCP[i].slaves.Remove(oldLabel))
                            {
                                ActualConfig.TCP[i].slaves.Add(slave.label);
                                Console.WriteLine("Rinominato slave: " + slave.label + " con profilo TCP: " + ActualConfig.TCP[i].label);
                            }
                        }

                        // Triggero l'event0 selectionChanged in modo da ricaricare le listbox RTU e TCP
                        DataGridHeadTcp_SelectedCellsChanged(null, null);
                        DataGridHeadRtu_SelectedCellsChanged(null, null);

                        // Aggiorno lista profili client
                        AggiornaProfiliListAndComboBox();

                        for (int i = 0; i < ComboBoxProfiles.Items.Count; i++)
                        {
                            if (ComboBoxProfiles.Items[i].ToString() == window.fileName)
                            {
                                ComboBoxProfiles.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
        }

        public void DuplicateProfile(MOD_SlaveProfile slave)
        {
            if (slave != null)
            {
                MOD_SlaveProfile newSlave = new MOD_SlaveProfile();

                newSlave.slave_id = slave.slave_id;

                int newValue = 0;
                String oldLabel = slave.label;
                String newLabel = "";
                bool checkIfProfileExist;

                // Se il profilo ha un underscore controllo che non abbia un index
                if (slave.label.IndexOf("_") != -1)
                {
                    // Se l'ultimo è un numero vado incrementale e rigenero la stringa oldLabel
                    if (int.TryParse(slave.label.Substring(slave.label.LastIndexOf("_") + 1, slave.label.Length - (slave.label.LastIndexOf("_") + 1)), out newValue))
                    {
                        oldLabel = slave.label.Substring(0, slave.label.LastIndexOf("_"));
                    }
                }

                do
                {
                    checkIfProfileExist = false;
                    newValue += 1;  // Incremento e poi controlllo che non esista
                    newLabel = oldLabel + "_" + newValue.ToString();

                    if (ActualConfig.profiles.Find(tmp => tmp.label == newLabel) != null)
                    {
                        checkIfProfileExist = true;
                    }
                }
                while (checkIfProfileExist);
                                
                newSlave.label = newLabel;
                newSlave.notes = slave.notes;

                if (slave.di != null)
                {
                    newSlave.di = new MOD_RegProfile();
                    newSlave.di.len = slave.di.len;

                    newSlave.di.data = new List<MOD_Reg>();

                    foreach (MOD_Reg mod in slave.di.data)
                    {
                        MOD_Reg new_ = new MOD_Reg();

                        new_.label = mod.label;
                        new_.reg = mod.reg;
                        new_.value = mod.value;

                        newSlave.di.data.Add(new_);
                    }
                }
                else
                {
                    newSlave.di = null;
                }

                if (slave.co != null)
                {
                    newSlave.co = new MOD_RegProfile();
                    newSlave.co.len = slave.co.len;

                    newSlave.co.data = new List<MOD_Reg>();

                    foreach (MOD_Reg mod in slave.co.data)
                    {
                        MOD_Reg new_ = new MOD_Reg();

                        new_.label = mod.label;
                        new_.reg = mod.reg;
                        new_.value = mod.value;

                        newSlave.co.data.Add(new_);
                    }
                }
                else
                {
                    newSlave.co = null;
                }

                if (slave.ir != null)
                {
                    newSlave.ir = new MOD_RegProfile();
                    newSlave.ir.len = slave.ir.len;

                    newSlave.ir.data = new List<MOD_Reg>();

                    foreach (MOD_Reg mod in slave.ir.data)
                    {
                        MOD_Reg new_ = new MOD_Reg();

                        new_.label = mod.label;
                        new_.reg = mod.reg;
                        new_.value = mod.value;

                        newSlave.ir.data.Add(new_);
                    }
                }
                else
                {
                    newSlave.ir = null;
                }

                if (slave.hr != null)
                {
                    newSlave.hr = new MOD_RegProfile();
                    newSlave.hr.len = slave.hr.len;

                    newSlave.hr.data = new List<MOD_Reg>();

                    foreach (MOD_Reg mod in slave.hr.data)
                    {
                        MOD_Reg new_ = new MOD_Reg();

                        new_.label = mod.label;
                        new_.reg = mod.reg;
                        new_.value = mod.value;

                        newSlave.hr.data.Add(new_);
                    }
                }
                else
                {
                    newSlave.hr = null;
                }

                ActualConfig.profiles.Add(newSlave);

                // Triggero l'event0 selectionChanged in modo da ricaricare le listbox RTU e TCP
                DataGridHeadTcp_SelectedCellsChanged(null, null);
                DataGridHeadRtu_SelectedCellsChanged(null, null);

                // Aggiorno lista profili client
                AggiornaProfiliListAndComboBox();

                for (int i = 0; i < ComboBoxProfiles.Items.Count; i++)
                {
                    if (ComboBoxProfiles.Items[i].ToString() == newSlave.label)
                    {
                        ComboBoxProfiles.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // -------- Funzione equivalnete alla vecchia Application.DoEvents() per WinForms ------
        // -------------------------------------------------------------------------------------
        public static void DoEvents()
        {
            Application.Current.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(delegate { }));
        }

        private void ButtonDuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboBoxProfiles.SelectedItem != null)
            {
                DuplicateProfile(ActualConfig.profiles.Find(tmp => tmp.label == ComboBoxProfiles.SelectedItem.ToString()));
            }
        }

        private void CheckBoxAddAddressEth0_Checked(object sender, RoutedEventArgs e)
        {
            ActualConfig.assignIp = (bool)(CheckBoxAddAddressEth0.IsChecked);
        }

        private void ButtonPowerOff_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Spegnere il Raspberry?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (MessageBox.Show("Scrivere la configurazione attuale prima di disconnetttersi?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ButtonWriteLinux_Click(null, null);
                }

                Thread t = new Thread(new ThreadStart(PowerOff));
                t.Start();
            }
        }

        public void PowerOff()
        {
            ButtonPowerOff.Dispatcher.Invoke((Action)delegate
            {
                ButtonPowerOff.IsEnabled = false;
            });

            closeTailLog();

            var cmd = sshClient.CreateCommand("sudo poweroff");

            try
            {
                string output = cmd.Execute();
            }
            catch
            {

            }

            try
            {
                sshClient.Disconnect();
            }
            catch (Exception err)
            {

            }

            ButtonWriteLinux.Dispatcher.Invoke((Action)delegate
            {
                connected = false;

                TextBlockConnectLinux.Text = "Connect";
                RadioButtonWindows.IsEnabled = true;
                RadioButtonRaspberry.IsEnabled = true;

                ButtonWriteLinux.IsEnabled = false;
                ButtonReadLinux.IsEnabled = connected;
                ButtonStartService.IsEnabled = connected;
                ButtonStopService.IsEnabled = connected;
                ButtonRestartService.IsEnabled = connected;

                TextBoxAddress.IsEnabled = !connected;
                TextBoxPort.IsEnabled = !connected;
                TextBoxUsername.IsEnabled = !connected;
                TextBoxPassword.IsEnabled = !connected;

                ButtonPowerOff.IsEnabled = connected;
                ButtonSendManualCommand.IsEnabled = connected;

                ButtonDownloadFromRaspberry.IsEnabled = connected;
                ButtonUploadToRasperry.IsEnabled = connected;
                ButtonPowerOffRaspberryConsole.IsEnabled = connected;

                ButtonImportAllConfiguration.IsEnabled = connected;
                ButtonExportAllConfiguration.IsEnabled = connected;
            });
        }

        private void ButtonSendManualCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cmd = sshClient.CreateCommand(TextBoxManualCommand.Text);

                string output = cmd.Execute();

                RichTextBoxOutput.AppendText("\n");
                RichTextBoxOutput.AppendText("Command > " + cmd.CommandText + "\n");
                RichTextBoxOutput.AppendText(output);
                RichTextBoxOutput.ScrollToEnd();
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
            }
        }

        private void TextBoxManualCommand_KeyUp(object sender, KeyEventArgs e)
        {
            if (connected)
            {
                if (e.Key == Key.Enter)
                {
                    ButtonSendManualCommand_Click(null, null);
                }
            }
        }

        private void ButtonUploadToRasperry_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Caricare la configurazione locale di windows sul Raspberry?\n\n(l'operazione sovrascriverà irreversibilemente la configurazione del Raspberry)", "Alert", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Copio il file di configurazione LocalWindows nella cartella tmp\read come se fosse stato appena scaricato
                File.Copy(Directory.GetCurrentDirectory() + "\\PythonEngine\\config.json", Directory.GetCurrentDirectory() + "\\tmp\\read\\config.json", true);

                // Leggo il file come se fosse stato scaricato
                readConfig(Directory.GetCurrentDirectory() + "\\tmp\\read\\config.json");

                // Abilito il button di scrittura qualora non fosse stato abilitato
                ButtonWriteLinux.IsEnabled = true;

                // Equivalente di un write su Raspberry
                ButtonWriteLinux_Click(null, null);
            }
            else
            {
                MessageBox.Show("Operazione annullata", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ButtonDownloadFromRaspberry_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Scaricare la configurazione dal Raspberry e sovrascrivere quella locale?\n\n(l'operazione sovrascriverà irreversibilemente la configurazione locale del client lato windows)", "Alert", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Equivalente di un read
                ButtonReadLinux_Click(null, null);

                // Copio il file locale appena scaricato nel programma per LocalWindows
                File.Copy(Directory.GetCurrentDirectory() + "\\tmp\\read\\config.json", Directory.GetCurrentDirectory() + "\\PythonEngine\\config.json", true);
            }
            else
            {
                MessageBox.Show("Operazione annullata", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ButtonImportAllConfiguration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();

                if (Directory.Exists(TextBoxOldPathImportExportConfig.Text))
                {
                    openFileDialog.InitialDirectory = TextBoxOldPathImportExportConfig.Text;
                }

                openFileDialog.Filter = "json files (*.json)|*.json";
                openFileDialog.RestoreDirectory = true;

                if ((bool)openFileDialog.ShowDialog())
                {
                    readConfig(openFileDialog.FileName);

                    // Salvo il path per il prossimo filedialog
                    TextBoxOldPathImportExportConfig.Text = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                }

                // Nel caso del Raspberry chiedo se inviare la configurazione
                if (!(bool)RadioButtonWindows.IsChecked)
                {
                    if(MessageBox.Show("inviare la configurazione appena caricata al Raspberry?", "Question", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        ButtonWriteLinux_Click(null, null);
                    }
                }
                // Nel caso di windows chiedo se riavviare il servizio
                else
                {
                    // Se è attiva la finestra separata mando solo una notifica
                    if ((bool)CheckBoxOpenSeparateWindow.IsChecked)
                    {
                        MessageBox.Show("Chiudere il cmd e riavviare lo script per applicare le modifiche.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Se era in esecuzione chiedo se riavviarlo direttamente
                        if (modbusThreadRunning)
                        {
                            if(MessageBox.Show("Riavviare lo script per applicare le modifiche?", "Info", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                            {
                                ButtonRestartWindows_Click(null, null);
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Console.WriteLine(err);
                MessageBox.Show("Il file selezionato non è un file di configurazione valido.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Se fallisce la lettura della config ricarico l'ultima corrente altrimenti rimane null
                if ((bool)RadioButtonWindows.IsChecked)
                {
                    // Windows
                    LoadDefaultWindowsConfiguration();
                }
                else
                {
                    // Linux
                    ButtonReadLinux_Click(null, null);
                }
            }
        }

        private void ButtonExportAllConfiguration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();

                if (Directory.Exists(TextBoxOldPathImportExportConfig.Text))
                {
                    saveFileDialog.InitialDirectory = TextBoxOldPathImportExportConfig.Text;
                }

                saveFileDialog.Filter = "json files (*.json)|*.json";
                saveFileDialog.RestoreDirectory = true;

                if ((bool)saveFileDialog.ShowDialog())
                {
                    writeConfig(saveFileDialog.FileName);

                    // Salvo il path per il prossimo filedialog
                    TextBoxOldPathImportExportConfig.Text = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                }
            }
            catch(Exception err)
            {
                Console.WriteLine(err);
                MessageBox.Show("Errore salvataggio configurazione", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonRestartWindows_Click(object sender, RoutedEventArgs e)
        {
            ButtonStopWindowsThread_Click(null, null);
            Thread.Sleep(100);
            DoEvents();
            ButtonStartWindowsThread_Click(null, null);
        }

        private void CheckBoxOpenSeparateWindow_Checked(object sender, RoutedEventArgs e)
        {
            ButtonStopWindowsThread.Visibility = (bool)CheckBoxOpenSeparateWindow.IsChecked ? Visibility.Hidden : Visibility.Visible;
            ButtonRestartWindowsThread.Visibility = (bool)CheckBoxOpenSeparateWindow.IsChecked ? Visibility.Hidden : Visibility.Visible;
        }

        private void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            Info window = new Info();
            window.Owner = this;
            window.Show();
        }

        private void TextBoxTerminalRows_TextChanged(object sender, TextChangedEventArgs e)
        {
            uint value = 0;

            if(uint.TryParse(TextBoxTerminalRows.Text, out value))
            {
                rowMaxLog = value;
            }
            else
            {
                TextBoxTerminalRows.Text = "500";
            }
        }

        private void TextBoxTimeoutPing_TextChanged(object sender, TextChangedEventArgs e)
        {
            int output = 100;

            if(int.TryParse(TextBoxTimeoutPing.Text, out output))
            {
                pingTimeout = output;
            }
            else
            {
                TextBoxTimeoutPing.Text = "100"; // Rimetto default
            }
        }

        private void CheckBoxSelectAllTcp_Checked(object sender, RoutedEventArgs e)
        {
            foreach (MOD_HeadTcp head in ActualConfig.TCP)
            {
                head.enable = (bool)CheckBoxSelectAllTcp.IsChecked;
            }

            DataGridHeadTcp.ItemsSource = null;
            DataGridHeadTcp.ItemsSource = ActualConfig.TCP;
        }

        private void CheckBoxSelectAllRtu_Checked(object sender, RoutedEventArgs e)
        {
            foreach (MOD_HeadRtu head in ActualConfig.RTU)
            {
                head.enable = (bool)CheckBoxSelectAllRtu.IsChecked;
            }

            DataGridHeadRtu.ItemsSource = null;
            DataGridHeadRtu.ItemsSource = ActualConfig.RTU;
        }

        private void TextBoxUsername_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}