using System;
using Windows.Foundation.Metadata;

namespace VK_UI3
{
    internal class StaticParams
    {
        public static readonly string tokenStatDefaultGate = "i5QlRbdyTpWgWgiNDBysitL88xUswcWAfQSFVWwxj5pwMdcl7KrNBfK0Qk9r";
    }

    public class MusicMStatDefaultGate : StatDefaultGateLib.StatDefaultGate
    {
        public  static string Token { get; set; } = StaticParams.tokenStatDefaultGate;
        public MusicMStatDefaultGate() : base(Token) 
        {
        }
    }
}