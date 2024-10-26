using System;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Runtime.Serialization.Formatters.Binary;

namespace Client
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Настройки подключения
        private static string IPAddress; // поле для присвоения ему значения IP-адреса, вводимого в "IPBox" клиентом
        private static int port = 55000; // задание значения порта, равному 55000 (может использоваться любой свободной порт)        
        #endregion

        #region Прочие настройки
        private static IPEndPoint remoteEP; // поле для хранения информации об IP-адресе и порте
        private static Socket client; // любое внешнее взаимодействие с клиентом
        private static byte[] packetSerialize; // представление преобразованных данных
        private const string disconnectMessage = "\nРазрыв соединения!"; // системное уведомление для корректной отработки процесса отключения клиента
        #endregion

        #region Синхронизация потока
        /// <summary>
        /// Служат для управления процессом выполнения приложения - ожидание выполнения какой-либо программы.
        /// Также возможно использование класса AutoResetEvent.
        /// </summary>
        private static readonly ManualResetEvent connectDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent sendDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent receiveDone = new ManualResetEvent(false);
        #endregion

        #region Инициализация окна и его закрытие
        /// <summary>
        /// Создание окна и вывод стартовой информации.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            WriteStatus("Здравствуйте!"
                + "\nДля подключения к серверу необходимо ввести его IP-адрес и нажать кнопку 'Подключиться'."
                + "\nP.S. Для получения адреса обратитесь к администратору сервера.");
        }

        /// <summary>
        /// Обработка закрытия программы.
        /// Если программа закрывается, то происходит отправка сообщения на сервер о прекращении связи.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBox.Show("Вы действительно хотите выйти?", "Выход", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }
        #endregion

        #region Подключение к серверу (в общем контексте)
        /// <summary>
        /// Считывание данных с полей ввода IP-адреса и порта (задан изначально), их проверка.
        /// Вызов функции StartClient.
        /// </summary>
        private void StartUp()
        {
            ConsoleClientTextBox.Text = "Невозможно подключиться к серверу!"
                + "\nВозможно написанные ниже сообщения не являются истиной!"
                + "\n----------\n";

            if (IPBox.Text != string.Empty && PortBox.Text != string.Empty) // проверка на корректность вводимых данных
            {
                IPAddress = IPBox.Text;
                port = int.Parse(PortBox.Text);

                bool result;
                result = StartClient();

                if (result)
                {
                    ConnectButton.Content = "Отключиться";
                    ConsoleClientTextBox.Clear();

                    IPBox.IsEnabled = false;
                    FirstLastNameBox.IsEnabled = true;
                    UniversityBox.IsEnabled = true;
                    PhoneBox.IsEnabled = true;
                    browseButton.IsEnabled = true;
                    SendButton.IsEnabled = true;
                }
                else
                {
                    ConnectButton.Content = "Подключиться";
                }
            }
            else
            {
                _ = MessageBox.Show("Введите IP адрес и порт!", "Ошибка!");
            }
        }
        #endregion

        #region Создание сокета, подключение, отключение
        /// <summary>
        /// Создание сокета, привязка к IP-адресу, выполнение попытки подключения.
        /// </summary>
        /// <returns>Результат операции.</returns>
        public bool StartClient()
        {
            _ = connectDone.Reset();

            if (ClientPrepare()) // запуск подготовки соединения
            {
                try
                {
                    _ = client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), client);
                    _ = connectDone.WaitOne(); // ожидание установления подключения
                    if (client.Connected)
                    {
                        return true;
                    }
                    else
                    {
                        WriteStatus("Сокет отключен!");

                        return false;
                    }
                }
                catch (SocketException exc)
                {
                    _ = MessageBox.Show(exc.Message, "Ошибка!");
                    WriteStatus("Ошибка подключения!");

                    return false;
                }
                catch (Exception exc)
                {
                    _ = MessageBox.Show(exc.Message, "Ошибка!");

                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Создание сокета, подключение (определение) IP-адреса сервера, к которому производится попытка подключения.
        /// </summary>
        private bool ClientPrepare()
        {
            try
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(IPAddress);
                IPAddress IPAddr = ipHostInfo.AddressList[0];

                foreach (IPAddress addr in ipHostInfo.AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        IPAddr = addr;
                        break;
                    }
                }

                remoteEP = new IPEndPoint(IPAddr, port);
                WriteStatus("Подключение выполнено!\nIP-адрес сервера: " + remoteEP.Address.ToString() + "\nПорт: " + remoteEP.Port.ToString());

                client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                WriteStatus("----------\nДля получения логина и пароля выполните следующие действия:"
                    + "\n1) запишите свои персональные данные в соответствующие поля;"
                    + "\n2) прикрепите документ, подтверждающий личность;"
                    + "\n3) нажмите на кнопку 'Отправить данные'.");

                return true;
            }
            catch (SocketException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");

                return false;
            }
        }

        /// <summary>
        /// Функция обратной связи при успешном подключении к серверу.
        /// </summary>
        /// <param name="ar">Содержит результат асинхронного запроса (оболочка над данными).</param>
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState; // получание сокета из результирующих данных
                client.EndConnect(ar); // закрытия соединения (потока)
            }
            catch (SocketException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
                client.Close();
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
            }
            finally
            {
                _ = connectDone.Set(); // сигнализация основному потоку о возможности продолжения
            }
        }

        /// <summary>
        /// Завершение подключения, вывод соответствующего сообщения.
        /// </summary>
        private void Shutdown()
        {
            client.Shutdown(SocketShutdown.Both);
            client.Close();
        }
        #endregion

        #region Работа с данными (подготовка, преобразование)
        /// <summary>
        /// Подготовка данных для отправки.
        /// </summary>
        /// <param name="user">Информация о пользователе.</param>
        /// <param name="pathToFile">Путь к файлу.</param>
        private void PrepareData(UserInfo user, string pathToFile)
        {
            MyPacketWrapper myPacket = new MyPacketWrapper();

            try
            {
                myPacket.FileBuff = File.ReadAllBytes(pathToFile);
                myPacket.FileName = Path.GetFileName(pathToFile);
                myPacket.UserDetails = user;

                SerializeMyPacket(myPacket);
            }
            catch (FileNotFoundException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
            }
        }

        /// <summary>
        /// Преобразование текущего объекта.
        /// </summary>
        private void SerializeMyPacket(MyPacketWrapper currentPacket)
        {
            MemoryStream ms = new MemoryStream();
            BinaryFormatter serializer = new BinaryFormatter();

            serializer.Serialize(ms, currentPacket);
            packetSerialize = ms.ToArray();
            ms.Close();

            WriteStatus("----------\nПреобразование данных завершено. Данные переданы администратору."
                + "\nРазмер отправленного объема данных: " + packetSerialize.Length + " байт");
        }
        #endregion

        #region Отправление и прием данных, обратная связь
        private bool Send(byte[] packetSerialize)
        {
            _ = sendDone.Reset();
            _ = receiveDone.Reset();

            try
            {
                _ = client.BeginSend(packetSerialize, 0, packetSerialize.Length, 0, new AsyncCallback(SendCallback), client);
                _ = sendDone.WaitOne();

                return true;
            }
            catch (SocketException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
                _ = MessageBox.Show("Проблема с соединением! Попробуйте переподключиться.", "Ошибка!");

                ConnectButton.Content = "Подключиться";
                WriteStatus("Проблемы с соединением!");

                return false;
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");

                return false;
            }
        }
        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                int bytesSent = client.EndSend(ar);

                _ = sendDone.Set();
            }
            catch (Exception e)
            {
                _ = MessageBox.Show(e.ToString(), "Ошибка!");
            }
        }
        private void Receive()
        {
            try
            {
                StateObject state = new StateObject
                {
                    workSocket = client
                };

                _ = client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                _ = receiveDone.Set();

                StateObject state = (StateObject)ar.AsyncState;
                Socket MyClient = state.workSocket;

                int bytesRead = MyClient.EndReceive(ar);

                if (bytesRead > 0)
                {
                    string result = Encoding.UTF8.GetString(state.buffer, 0, bytesRead);
                    WriteStatus(result);

                    Shutdown();

                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                        (ThreadStart)delegate ()
                        {
                            ConnectButton.Content = "Подключиться";
                            ConnectButton.IsEnabled = false;
                        }
                    );

                    WriteStatus("\nВы отключены от сервера!");
                }
                else
                {
                    _ = MessageBox.Show("Отсутствуют данные для получения!", "Ошибка!");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        #endregion

        #region Работа кнопок
        /// <summary>
        /// Подключение к серверу.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (client == null || (client.Connected == false))
            {
                StartUp();
            }
            else
            {
                if (client.Connected)
                {
                    ConnectButton.Content = "Подключиться";
                    ConsoleClientTextBox.Clear();
                    WriteStatus("Разрыв соеднинения с сервером!");

                    IPBox.IsEnabled = true;
                    FirstLastNameBox.IsEnabled = false;
                    UniversityBox.IsEnabled = false;
                    PhoneBox.IsEnabled = false;
                    browseButton.IsEnabled = false;
                    SendButton.IsEnabled = false;

                    byte[] message = Encoding.ASCII.GetBytes(disconnectMessage); // отправление сообщения об отключении на сервер
                    _ = Send(message);

                    Shutdown(); // завершение соединения
                }
                else
                {
                    StartUp();
                }
            }
        }

        /// <summary>
        /// Отправление данных.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (client != null)
            {
                if (client.Connected)
                {
                    string path;

                    try
                    {
                        path = FileNameTextBox.Text;

                        if (FirstLastNameBox.Text != string.Empty
                            && UniversityBox.Text != string.Empty
                            && PhoneBox.Text != string.Empty
                            && path != string.Empty) // условия для запуска обработки и отправки данных
                        {
                            UserInfo user = new UserInfo(FirstLastNameBox.Text, UniversityBox.Text, PhoneBox.Text); // получение и подготовка данных

                            if (File.Exists(path))
                            {
                                PrepareData(user, path);

                                byte[] info = Encoding.ASCII.GetBytes(packetSerialize.Length.ToString());
                                bool isConnected = Send(info); // отправка информации о размере (или об отключении)

                                if (isConnected)
                                {
                                    if (client.Connected)
                                    {
                                        isConnected = Send(packetSerialize);

                                        if (isConnected)
                                        {
                                            WriteStatus("\nОжидание ответа...");

                                            Task receiveTask = Task.Factory.StartNew(Receive);

                                            FirstLastNameBox.IsEnabled = false;
                                            FirstLastNameBox.Clear();
                                            UniversityBox.IsEnabled = false;
                                            UniversityBox.Clear();
                                            PhoneBox.IsEnabled = false;
                                            PhoneBox.Clear();
                                            FileNameTextBox.Clear();
                                            browseButton.IsEnabled = false;
                                            SendButton.IsEnabled = false;
                                            MyPic.Visibility = Visibility.Hidden;
                                            MyPic.Visibility = Visibility.Hidden;
                                        }
                                    }
                                    else
                                    {
                                        WriteStatus("\n\nСоединение было разорвано!");
                                        ConnectButton.Content = "Подключиться";

                                        Shutdown();
                                    }
                                }
                            }
                            else
                            {
                                _ = MessageBox.Show("Текущий путь до файла не корректен, либо файл отсуствует!", "Ошибка!");
                            }
                        }
                        else
                        {
                            _ = MessageBox.Show("Заполните все поля ввода данных!", "Ошибка!");
                        }
                    }
                    catch (Exception)
                    {
                        _ = MessageBox.Show("Неизвестная ошибка!", "Ошибка!");
                    }
                }
                else // отсутствие подключения
                {
                    _ = MessageBox.Show("Проблема с соединением. Попробуйте переподключиться.", "Ошибка!");
                    ConnectButton.Content = "Подключиться";
                }
            }
            else
            {
                _ = MessageBox.Show("Сначала необходимо подключиться!", "Ошибка!");
            }
        }

        /// <summary>
        /// Открытие диалогового окна с выбором пути.
        /// Путь записывается в поле FileNameTextBox (доступно только для чтения и служит для подтверждения прикрепления файла).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                try
                {
                    MyPic.Source = new BitmapImage(new Uri(dlg.FileName, UriKind.RelativeOrAbsolute));

                    FileNameTextBox.Text = dlg.FileName;
                    SendButton.IsEnabled = true;
                }
                catch (Exception)
                {
                    MyPic.Source = new BitmapImage();

                    FileNameTextBox.Clear();
                    SendButton.IsEnabled = false;
                    _ = MessageBox.Show("Загруженный файл не является изображением! Выберите корректный файл.", "Ошибка!");
                }
            }
        }
        #endregion

        #region Отображение информации в "Окне вывода"
        /// <summary>
        /// Вывод информации в "псевдо-консоль" при помощи Dispatcher.
        /// </summary>
        /// <param name="info">Строка, которую необходимо отобразить.</param>
        private void WriteStatus(string info)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                (ThreadStart)delegate ()
                {
                    ConsoleClientTextBox.Text += info + "\n";
                    ConsoleTextBoxClientScroll.ScrollToEnd();
                    ConsoleClientTextBox.UpdateLayout();
                }
            );
        }
        #endregion
    }
}