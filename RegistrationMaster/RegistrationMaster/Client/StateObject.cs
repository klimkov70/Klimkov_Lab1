using System.Text;
using System.Net.Sockets;

namespace Client
{
    /// <summary>
    /// Состояние объекта для приема данных с удаленного устройства.
    /// </summary>
    public class StateObject
    {
        public Socket workSocket = null; // клиентский сокет

        public const int BufferSize = 1024; // размер буфера приема

        public byte[] buffer = new byte[BufferSize]; // буфер приема

        public StringBuilder sb = new StringBuilder(); // полученные данные
    }
}
