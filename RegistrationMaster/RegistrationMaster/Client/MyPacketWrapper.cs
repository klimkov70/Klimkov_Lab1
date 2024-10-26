using System;

namespace Client
{
    /// <summary>
    /// Класс MyPacketWrapper:IPacketWrapper.
    /// </summary>
    [Serializable]
    public class MyPacketWrapper
    {
        public byte[] FileBuff { get; set; }
        public string FileName { get; set; }
        public UserInfo UserDetails { get; set; }
    }
}
