using System;
using Windows.Foundation.Metadata;

namespace VK_UI3
{
    internal class StaticParams
    {
        public static readonly string tokenStatDefaultGate = Environment.GetEnvironmentVariable("TOKEN_STAT_DEFAULT_GATE");
    }

    public class MusicMStatDefaultGate : StatDefaultGateLib.StatDefaultGate
    {
        public  static string Token { get; set; } = StaticParams.tokenStatDefaultGate;
        public MusicMStatDefaultGate() : base(Token) 
        {
        }
    }
}