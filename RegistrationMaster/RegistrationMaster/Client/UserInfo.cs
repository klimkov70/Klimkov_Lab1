using System;

namespace Client
{
    [Serializable]
    public class UserInfo
    {
        public UserInfo(string name, string university, string phone)
        {
            _fullName = name;
            _university = university;
            _phone = phone;
        }

        /// <summary>
        /// ФИО пользователя
        /// </summary>
        private readonly string _fullName;
        public string FullName => _fullName;

        /// <summary>
        /// Название учебного заведения
        /// </summary>
        private readonly string _university;
        public string University => _university;

        /// <summary>
        /// Телефон пользователя
        /// </summary>
        private readonly string _phone;
        public string Phone => _phone;
    }
}
