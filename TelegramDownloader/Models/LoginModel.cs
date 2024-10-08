﻿using TL;

namespace TelegramDownloader.Models
{
    public class LoginModel
    {
        public string type {  get; set; }
        public string value { get; set; }
    }

    public class UserData
    {
        public UserData(string phone) {
            this.phone = phone;
        }

        public string phone { get; set; }
    }

    public class ChatViewBase
    {
        public ChatBase chat { get; set; }
        public string img64 { get; set; }

    }

    public class ChatMessages
    {
        public Message message { get; set; }
        public string htmlMessage { get; set; }
        public IPeerInfo user { get; set; }
        public string videoThumb { get; set; }
        public bool isDocument {  get; set; }
    }
}
