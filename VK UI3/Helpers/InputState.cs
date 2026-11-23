using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace VK_UI3.Helpers
{
    /// <summary>
    /// Единая точка определения зажатых клавиш-модификаторов.
    /// Состояние читается в момент вызова (а не из флага), поэтому работает
    /// независимо от того, откуда пришло действие: клик, клавиатура, тач.
    /// </summary>
    public static class InputState
    {
        public static bool IsCtrlPressed()
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        public static bool IsShiftPressed()
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        /// <summary>Ctrl зажат — действие должно открыть новую вкладку.</summary>
        public static bool ForceNewTab => IsCtrlPressed();
    }
}
