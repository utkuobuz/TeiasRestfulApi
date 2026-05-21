using System;
using System.Collections.Generic;
using System.Text;

namespace TEİASRestfulApi.DTOs
{
    public class LoginResponse
    {
        public bool? basarili { get; set; }
        public string? jeton { get; set; } // En önemli parça bu! [cite: 536]
        public List<string>? mesaj { get; set; }
    }
}
