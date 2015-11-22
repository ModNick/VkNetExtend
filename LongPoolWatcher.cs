﻿namespace VkNetExtend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using VkNet;
    using VkNet.Model;
    using VkNet.Exception;
    using VkNet.Model.RequestParams.Messages;

    public delegate void MessagesRecievedDelegate(IEnumerable<Message> messages, long accountID);

    public class LongPoolWatcher
    {
        private VkApi _account;

        private GetLongPollHistoryParams reqParams;

        public bool Active { get; private set; }

        #region Управление слежением
        private Timer _watchTimer;

        public byte MaxSleepSteps = 3;
        public int SteepSleepTime = 333;
        private byte _currentSleepSteps = 1;
        #endregion

        public static event MessagesRecievedDelegate onNewMessages;

        public LongPoolWatcher(VkApi api)
        {
            _account = api;
            reqParams = new GetLongPollHistoryParams();
        }

        private LongPollServerResponse GetLongPoolServer(ulong? lastPts = null)
        {
            var response = _account.Messages.GetLongPollServer(false, lastPts == null);

            reqParams.Ts = response.Ts;
            reqParams.Pts = lastPts == null ? response.Pts : lastPts;

            return response;
        }
        private Task<LongPollServerResponse> GetLongPoolServerAsync(ulong? lastPts = null)
        {
            return Task.Run(() => { return GetLongPoolServer(lastPts); });
        }

        private LongPollHistoryResponse GetLongPoolHistory()
        {
            int c = 0;
            LongPollHistoryResponse history = null;
            string errorLog = "";

            while (c < 5 && history == null)
            {
                c++;
                try
                {
                    history = _account.Messages.GetLongPollHistory(reqParams);
                }
                catch (TooManyRequestsException)
                {
                    Thread.Sleep(150);
                }
                catch (Exception ex)
                {
                    errorLog += string.Format("{0} - {1}{2}", c, ex.Message, Environment.NewLine);
                }
            }

            if (history != null)
                reqParams.Pts = history.NewPts;
            else
                throw new NotImplementedException(errorLog);

            return history;
        }
        private Task<LongPollHistoryResponse> GetLongPoolHistoryAsync()
        {
            return Task.Run(() => { return GetLongPoolHistory(); });
        }

        public Task<List<Message>> LoadDialogsAsync(int offset, int count = 20)
        {
            if (_account == null || string.IsNullOrEmpty(_account.AccessToken))
                throw new NotImplementedException("Не авторизован в API ВК");

            return Task.Run(() =>
            {
                int total;
                var dialogs = _account.Messages.GetDialogs(out total, out total, count, offset);
                return dialogs.ToList();
            });
        }

        private async void _watchAsync(object state)
        {
            var history = await GetLongPoolHistoryAsync();
            if (history.Messages.Count > 0)
            {
                _currentSleepSteps = 1;
                if (onNewMessages != null)
                    onNewMessages(history.Messages, _account.UserId.Value);
            }
            else if (_currentSleepSteps < MaxSleepSteps)
                _currentSleepSteps++;

            _watchTimer.Change(_currentSleepSteps * SteepSleepTime, Timeout.Infinite);
        }

        public async void StartAsync(ulong? lastTs = null, ulong? lastPts = null)
        {
            if (Active)
                throw new NotImplementedException("Messages for {0} already watching");

            Active = true;
            await GetLongPoolServerAsync(lastPts);

            _watchTimer = new Timer(new TimerCallback(_watchAsync), null, 0, Timeout.Infinite);
        }
        public void Stop()
        {
            if (_watchTimer != null)
                _watchTimer.Dispose();
            Active = false;
            _watchTimer = null;
        }
    }
}
