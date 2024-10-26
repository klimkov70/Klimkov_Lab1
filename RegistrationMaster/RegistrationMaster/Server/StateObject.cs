using System.Text;
using System.Net.Sockets;

namespace Server
{
    /// <summary>
    /// Объект состояния для асинхронного чтения клиентских данных.
    /// </summary>    
    public class StateObject
    {
        public Socket workSocket = null; // клиентский сокет

        public const int BufferSize = 1024; // размер буфера приема

        public byte[] buffer = new byte[BufferSize]; // буфер приема

        public StringBuilder sb = new StringBuilder(); // полученные данные

        public int clientNum = 1;

        public long sizeReceived = 0;

        public static int countClient = 0;

        public long sizePacket = 0;

    }
}
