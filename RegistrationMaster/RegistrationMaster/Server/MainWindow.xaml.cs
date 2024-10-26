using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;

namespace Server
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Настройки подключения
        private const int port = 55000; // задание значения порта, равному 55000 (может использоваться любой свободной порт)
        private const int socketSize = 100; // размер сокета
        #endregion

        #region Прочие настройки
        private string baseDir; // директория для сохранения принятых документов от пользователей
        private static IPEndPoint localEndPoint; // конечная точка подключения
        private static Socket listener; // серверный сокет для прослушки входящих соединений
        private static readonly List<StateObject> clients = new List<StateObject>(); // список подключенных клиентов
        private const string disconnectMessage = "\nРазрыв соединения!"; // системное уведомление для корректной отработки процесса отключения сервера
        #endregion

        #region Синхронизация потока
        /// <summary>
        /// Служат для управления процессом выполнения приложения - ожидание выполнения какой-либо программы.
        /// Также возможно использование класса AutoResetEvent.
        /// </summary>
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent infoDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent sendDone = new ManualResetEvent(false);
        #endregion

        #region Инициализация окна и его закрытие
        /// <summary>
        /// Создание окна и вывод стартовой информации.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            CreateEmptyDir(); // очистка папки документов пользователей "DocsDirectory" при каждом запуске сервера

            Task listen = Task.Factory.StartNew(StartListening); // начало основной логики в новой задаче (потоке)

            WriteStatus("Администратор!"
                + "\nПосле окончания работы перенеси файлы документов пользователей из папки 'DocsDirectory' в отдельное место."
                + "\nПри следующем запуске сервера папка документов будет очищена!");
        }

        /// <summary>
        /// Обработка закрытия программы.
        /// Если программа закрывается, то происходит отправка сообщения всем клиентам в локальной сети о прекращении связи.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBox.Show("Вы действительно хотите выйти?", "Выход", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
            else
            {
                foreach (StateObject client in clients)
                {
                    _ = Send(client.workSocket, disconnectMessage);
                    client.workSocket.Shutdown(SocketShutdown.Both);
                    client.workSocket.Close();
                }
            }
        }
        #endregion

        #region Запуск сервера, начало прослушки всех входящих соеднинений
        /// <summary>
        /// Создание сокета, подключение (определение) IP-адреса сервера.
        /// </summary>
        private bool ServerPrepare()
        {
            try
            {
                localEndPoint = new IPEndPoint(IPAddress.Any, port);

                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); // создание сокета

                WriteStatus("----------\nСокет создан!"
                    + "\nСообщите IP-адрес сервера пользователям в локальной сети."
                    + "\nP.S. Для получения IPv4-адреса введите 'ipconfig' в командной строке сервера.");

                return true;
            }
            catch (SocketException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");

                return false;
            }
        }

        /// <summary>
        /// Привязка сокета к классу IPEndPoint.
        /// Определение количества одновременных подклчений.
        /// Начало прослушивания.
        /// </summary>
        public void StartListening()
        {
            if (ServerPrepare())
            {

                try
                {
                    listener.Bind(localEndPoint);
                    listener.Listen(socketSize);

                    WriteStatus("\nОжидание подключений...");

                    AcceptConncetions();
                }
                catch (Exception e)
                {
                    _ = MessageBox.Show(e.ToString(), "Ошибка!");
                }
            }

        }
        public void AcceptConncetions()
        {
            while (true)
            {
                _ = allDone.Reset();
                _ = listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
                _ = allDone.WaitOne();
            }
        }
        #endregion

        #region Обратная связь подключения, получение файлов, вывод информации о клиенте в консоль
        /// <summary>
        /// Получение подключения, занесение его в объект состояния и общий список всех подключений
        /// </summary>
        /// <param name="ar"></param>
        public void AcceptCallback(IAsyncResult ar)
        {
            _ = allDone.Set(); // сигнализация основному потоку о возможности продолжения

            Socket listener = (Socket)ar.AsyncState; // получение сокета, инициирующего подключение
            Socket handler = listener.EndAccept(ar); // завершение старого потока (получения сокета клиента)

            StateObject state = new StateObject
            {
                workSocket = handler,
                clientNum = StateObject.countClient // присвоение каждому клиенту своего номера
            }; // создание объекта состояния подключения
            StateObject.countClient++; // подсчет общего количества клиентов

            clients.Add(state); // добавление в общий спискок клиентов

            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                (ThreadStart)delegate () // добавление в "clientComboBox"
                {
                    _ = clientComboBox.Items.Add(state.clientNum);
                }
            );

            state.clientNum += 1;
            WriteStatus("----------\nПользователь №" + state.clientNum + " подключился.\n\nОжидание данных...");

            _ = infoDone.Reset();

            // принятие информации о размере пакета данных или сообщения о разрыве связи
            _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadInfoCallback), state);
            _ = infoDone.WaitOne();

            if (handler.Connected) // принятие основных данных, в случае успешной передачи данных о размере и присутствия соединения
            {
                _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
            }
            else
            {
                _ = clients.Remove(state); // удаление клиента из списка и из "clientComboBox"
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    (ThreadStart)delegate ()
                    {
                        clientComboBox.Items.Remove(state.clientNum);
                    }
                );

                WriteStatus("\nПользователь №" + state.clientNum + " отключился.");
            }
        }

        /// <summary>
        /// Получение размера файла.
        /// </summary>
        /// <param name="ar">Хранит в себе размер файла.</param>
        public void ReadInfoCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            try
            {
                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    string message = Encoding.ASCII.GetString(state.buffer, 0, bytesRead);
                    if (message != disconnectMessage)
                    {
                        try
                        {
                            state.sizePacket = long.Parse(message);
                        }
                        catch (Exception exc)
                        {
                            _ = MessageBox.Show(exc.Message, "Ошибка!");
                        }
                    }
                    else
                    {
                        state.sizePacket = -1;

                        handler.Shutdown(SocketShutdown.Both); // завершение подключения
                        handler.Close();
                    }
                }
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show("Потеряна связь при приеме данных! Ошибка: " + exc.Message, "Ошибка!");

                handler.Close();
            }
            finally
            {
                _ = infoDone.Set();
            }
        }

        /// <summary>
        /// Получение данных, запись их в бинарный файл
        /// </summary>
        /// <param name="ar">Представляет собой состояние асинхронной операции.</param>
        public void ReadCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            try // проверка наличия связи (с помощью handler.EndReceive - SocketException)
            {
                int bytesRead = handler.EndReceive(ar);
                state.sizeReceived += bytesRead;

                if (bytesRead > 0)
                {
                    string currentPath = baseDir + "Data" + state.clientNum + ".bin";

                    BinaryWriter writer = new BinaryWriter(File.Open(currentPath, FileMode.Append));

                    writer.Write(state.buffer, 0, bytesRead);
                    writer.Close();

                    if (state.sizePacket != state.sizeReceived) // принятие до совпадения размеров
                    {
                        _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                    }
                    else
                    {
                        Client.MyPacketWrapper myPacket = DeserializeData(currentPath);

                        WriteStatus("Данные пользователя №" + state.clientNum + " получены.");

                        SaveData(myPacket, state.clientNum);
                        ShowData(myPacket);
                    }
                }
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show("Потеряна связь при приеме данных! " + exc.Message, "Ошибка!");
            }
        }

        /// <summary>
        /// Отображение данных о пользователе в "псевдо-консоль".
        /// </summary>
        /// <param name="myPacket"></param>
        public void ShowData(Client.MyPacketWrapper myPacket)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                (ThreadStart)delegate ()
                {
                    WriteStatus("\nФИО: " + myPacket.UserDetails.FullName);
                    WriteStatus("Название учебного заведения: " + myPacket.UserDetails.University);
                    WriteStatus("Номер телефона: " + myPacket.UserDetails.Phone);
                    WriteStatus("Документ, удостоверяющий личность загружен в папку 'DocsDirectory'.");
                    WriteStatus("\nСоздайте логин и пароль, отправьте их пользователю...");
                }
            );
        }
        #endregion

        #region Отправление данных клиенту
        /// <summary>
        /// Приведение строковых данных к байтовому виду и отправление их по сети.
        /// </summary>
        /// <param name="handler">Текущий сокет для отправки.</param>
        /// <param name="data">Данные для отправки.</param>
        private bool Send(Socket handler, string data)
        {
            byte[] byteData = Encoding.UTF8.GetBytes(data);
            try
            {
                _ = handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);

                return true;
            }
            catch (SocketException exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
                WriteStatus("\n\nПроблемы с соединением!");

                return false;
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");

                return false;
            }
        }

        /// <summary>
        /// Функция обратной связи передачи информации и файлов пользователя.
        /// </summary>
        /// <param name="ar"></param>
        public void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket handler = (Socket)ar.AsyncState;
                int bytesSent = handler.EndSend(ar);

                WriteStatus("----------\nЛогин и пароль отправлены пользователю, размер отправленного объема данных: " + bytesSent + " байт");

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();

                WriteStatus("Пользователь отключен.");
            }
            catch (Exception e)
            {
                _ = MessageBox.Show(e.ToString(), "Ошибка!");
            }
            finally
            {
                _ = sendDone.Set();
            }
        }
        #endregion

        #region Работа кнопок
        /// <summary>
        /// Отправление данных выбранному клиенту.
        /// </summary>
        /// /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (clientComboBox.Items.Count != 0)
            {
                object selectItem = clientComboBox.SelectedItem; // определение номера выбранного клиента

                if (selectItem != null)
                {
                    int cbNum = (int)selectItem;

                    StateObject currentClient = clients.First((item) => item.clientNum == cbNum); // поиск нужного клиента в списке подлючений, его выбор

                    if (currentClient.sizePacket == currentClient.sizeReceived && currentClient.sizeReceived > 0) // если данные были корректно приняты
                    {
                        // считывание данных с полей ввода
                        string login = LoginBox.Text;
                        string password = PasswordBox.Text;

                        if (login != string.Empty && password != string.Empty)
                        {
                            string message = "\nАдминистратор успешно проверил информацию." +
                                "\nВаши авторизационные данные представлены ниже." +
                                "\n\n----------\nЛогин: " + login + "\n" + "Пароль: " + password + "\n----------"; // отправляемая строка данных

                            // отправление данных, закрытие подлючения
                            bool state = Send(currentClient.workSocket, message);

                            if (state == true)
                            {
                                _ = sendDone.WaitOne(); // ожидание завершения передачи
                            }

                            // удаление клиента из общего списка и из "clientComboBox" по завершению передачи данных
                            _ = clients.Remove(currentClient);
                            clientComboBox.Items.Remove(clientComboBox.SelectedItem);
                            LoginBox.Clear();
                            PasswordBox.Clear();
                        }
                        else
                        {
                            _ = MessageBox.Show("Введите логин и пароль!", "Ошибка!");
                        }
                    }
                    else
                    {
                        _ = MessageBox.Show("Сначала необходимо получить и проверить данные от пользователя (фотографию, документ)!", "Ошибка!");
                    }
                }
                else
                {
                    _ = MessageBox.Show("Выберите пользователя, которому хотите отправить данные!", "Ошибка!");
                }
            }
            else
            {
                _ = MessageBox.Show("Нет доступных подключений!", "Ошибка!");
            }
        }

        /// <summary>
        /// Открытие проводника для просмотра файлов пользователей.
        /// </summary>
        /// /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = Process.Start("explorer.exe", baseDir); // открытиие проводника в заданной директории
        }
        #endregion

        #region Отображение информации в "Консоли администратора"
        /// <summary>
        /// Вывод информации в "псевдо-консоль" при помощи Dispatcher.
        /// </summary>
        /// <param name="info">Строка, которую необходимо отобразить.</param>
        private void WriteStatus(string info)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                (ThreadStart)delegate ()
                {
                    ConsoleTextBoxServer.Text += info + "\n";
                    ConsoleTextBoxServerScroll.ScrollToEnd();
                    ConsoleTextBoxServer.UpdateLayout();
                }
            );
        }
        #endregion

        #region Вспомогательные функции
        /// <summary>
        /// Преоразование принятых данных.
        /// </summary>
        /// <param name="pathToFile">Путь к файлу.</param>
        /// <returns>Объект, представляющий файл и информацию о пользователе.</returns>
        public Client.MyPacketWrapper DeserializeData(string pathToFile)
        {
            byte[] buffer = File.ReadAllBytes(pathToFile);

            MemoryStream ms = new MemoryStream(); // проверка на количество доступной памяти
            ms.Write(buffer, 0, buffer.Length); // запись данных (buffer) в поток памяти
            BinaryFormatter serializer = new BinaryFormatter(); // получение объекта
            ms.Position = 0; // обязательно для корректного счета с начала
            Client.MyPacketWrapper myPacket = (Client.MyPacketWrapper)serializer.Deserialize(ms);

            ms.Close();

            WriteStatus("----------\nПреобразование данных завершено, размер полученного объема данных: " + buffer.Length + " байт");

            return myPacket;
        }

        /// <summary>
        /// Создание пустой папки для сохранения результатов, предварительно очищая папку с прошлого сеанса.
        /// </summary>
        private void CreateEmptyDir()
        {
            baseDir = Directory.GetCurrentDirectory() + "\\DocsDirectory\\";

            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }

            _ = Directory.CreateDirectory(baseDir);
        }

        /// <summary>
        /// Сохранение принятого бинарника.
        /// </summary>
        /// <param name="myPacket">Объект, представляющий собой пакет с данными.</param>
        /// <param name="clientNum">Номер клиента.</param>
        public void SaveData(Client.MyPacketWrapper myPacket, int clientNum)
        {
            try
            {
                string path = baseDir + "Пользователь №" + clientNum;

                if (!Directory.Exists(path))
                {
                    _ = Directory.CreateDirectory(path); // создание директории
                }

                string pathToFile = path + @"\" + myPacket.FileName;
                File.WriteAllBytes(pathToFile, myPacket.FileBuff);
            }
            catch (Exception exc)
            {
                _ = MessageBox.Show(exc.Message, "Ошибка!");
            }
        }
        #endregion
    }
}