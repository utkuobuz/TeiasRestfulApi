using System;
using System.Collections.Generic;
using System.Text;

namespace TEİASRestfulApi.DTOs
{
    public class LoginRequest
    {
        public string? kullaniciAdi { get; set; }
        public string? sifre { get; set; }
    }
}
