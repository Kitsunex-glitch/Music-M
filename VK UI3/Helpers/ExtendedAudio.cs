using System;
using VK_UI3.Controllers;
using VK_UI3.VKs.IVK;


namespace VK_UI3.Helpers
{
    public class ExtendedAudio
    {

        public class SelectedChange : EventArgs
        {
            public bool selected;

            public SelectedChange(bool selected)
            {
                this.selected = selected;
            }
        }

        public event EventHandler trackSelectChanged;

        public void trackSelectChangedInvoke(SelectedChange selectedChange)
        {
            if (trackSelectChanged != null)
                trackSelectChanged.Invoke(iVKGetAudio, selectedChange);
        }

        public ExtendedAudio(VkNet.Model.Attachments.Audio audio, IVKGetAudio iVKGetAudio)
        {
            this.audio = audio;
            this.iVKGetAudio = iVKGetAudio;
        }

        public bool PlayThis
        {
            get
            {
                // ВАЖНО: раньше здесь был _TrackDataThisGet().Result — синхронное
                // ожидание Task прямо в геттере. Геттер вызывается из обработчика
                // клика по треку на UI-потоке (TrackControl.UCcontrol_Tapped),
                // и если текущий трек ещё догружается (GetTrackPlay внутри делает
                // await), продолжение не может выполниться на занятом UI-потоке —
                // взаимная блокировка. Отсюда «подвисание UI при клике на трек».
                // PlayingTrack проставляется до вызова событий и уже содержит
                // нужный AccessKey, поэтому .Result не нужен.
                var trackdata = VK_UI3.Services.MediaPlayerService.PlayingTrack
                                ?? VK_UI3.Services.MediaPlayerService._trackDataThis;

                if (trackdata?.audio == null) return false;

                return trackdata.audio.AccessKey == this.audio.AccessKey;
            }
        }

        public VkNet.Model.Attachments.Audio audio { get; set; }

        private long? _numberInList;
        public long? NumberInList
        {
            get
            {
                if (_numberInList == null)
                {
                    iVKGetAudio.updateNumbers();
                }
                return _numberInList;
            }
            set
            {
                _numberInList = value;
            }
        }

        public IVKGetAudio iVKGetAudio { get; set; }
    }
}
