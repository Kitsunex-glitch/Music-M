using System;

namespace VK_UI3.Views.Notification
{
    public class InputField
    {
        public InputFieldType Type { get; set; } = InputFieldType.Text;
        public string Placeholder { get; set; } = "";
        public bool Required { get; set; } = false;
        public int? MaxLength { get; set; }
        public string DefaultValue { get; set; } = "";

        public InputField() { }

        public InputField(InputFieldType type, string placeholder = "", bool required = false, int? maxLength = null, string defaultValue = "")
        {
            Type = type;
            Placeholder = placeholder;
            Required = required;
            MaxLength = maxLength;
            DefaultValue = defaultValue;
        }
    }
}