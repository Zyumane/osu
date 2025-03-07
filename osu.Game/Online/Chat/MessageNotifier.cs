﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Online.Chat
{
    /// <summary>
    /// Component that handles creating and posting notifications for incoming messages.
    /// </summary>
    public class MessageNotifier : Component
    {
        [Resolved]
        private NotificationOverlay notifications { get; set; }

        [Resolved]
        private ChatOverlay chatOverlay { get; set; }

        [Resolved]
        private ChannelManager channelManager { get; set; }

        private Bindable<bool> notifyOnUsername;
        private Bindable<bool> notifyOnPrivateMessage;

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();
        private readonly IBindableList<Channel> joinedChannels = new BindableList<Channel>();

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, IAPIProvider api)
        {
            notifyOnUsername = config.GetBindable<bool>(OsuSetting.NotifyOnUsernameMentioned);
            notifyOnPrivateMessage = config.GetBindable<bool>(OsuSetting.NotifyOnPrivateMessage);

            localUser.BindTo(api.LocalUser);
            joinedChannels.BindTo(channelManager.JoinedChannels);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            joinedChannels.BindCollectionChanged(channelsChanged, true);
        }

        private void channelsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var channel in e.NewItems.Cast<Channel>())
                        channel.NewMessagesArrived += checkNewMessages;

                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (var channel in e.OldItems.Cast<Channel>())
                        channel.NewMessagesArrived -= checkNewMessages;

                    break;
            }
        }

        private void checkNewMessages(IEnumerable<Message> messages)
        {
            if (!messages.Any())
                return;

            var channel = channelManager.JoinedChannels.SingleOrDefault(c => c.Id == messages.First().ChannelId);

            if (channel == null)
                return;

            // Only send notifications, if ChatOverlay and the target channel aren't visible.
            if (chatOverlay.IsPresent && channelManager.CurrentChannel.Value == channel)
                return;

            foreach (var message in messages.OrderByDescending(m => m.Id))
            {
                // ignore messages that already have been read
                if (message.Id <= channel.LastReadId)
                    return;

                if (message.Sender.Id == localUser.Value.Id)
                    continue;

                // check for private messages first to avoid both posting two notifications about the same message
                if (checkForPMs(channel, message))
                    continue;

                checkForMentions(channel, message);
            }
        }

        /// <summary>
        /// Checks whether the user enabled private message notifications and whether specified <paramref name="message"/> is a direct message.
        /// </summary>
        /// <param name="channel">The channel associated to the <paramref name="message"/></param>
        /// <param name="message">The message to be checked</param>
        /// <returns>Whether a notification was fired.</returns>
        private bool checkForPMs(Channel channel, Message message)
        {
            if (!notifyOnPrivateMessage.Value || channel.Type != ChannelType.PM)
                return false;

            notifications.Post(new PrivateMessageNotification(message, channel));
            return true;
        }

        private void checkForMentions(Channel channel, Message message)
        {
            if (!notifyOnUsername.Value || !CheckContainsUsername(message.Content, localUser.Value.Username)) return;

            notifications.Post(new MentionNotification(message, channel));
        }

        /// <summary>
        /// Checks if <paramref name="message"/> mentions <paramref name="username"/>.
        /// This will match against the case where underscores are used instead of spaces (which is how osu-stable handles usernames with spaces).
        /// </summary>
        public static bool CheckContainsUsername(string message, string username)
        {
            string fullName = Regex.Escape(username);
            string underscoreName = Regex.Escape(username.Replace(' ', '_'));
            return Regex.IsMatch(message, $@"(^|\W)({fullName}|{underscoreName})($|\W)", RegexOptions.IgnoreCase);
        }

        public class PrivateMessageNotification : HighlightMessageNotification
        {
            public PrivateMessageNotification(Message message, Channel channel)
                : base(message, channel)
            {
                Icon = FontAwesome.Solid.Envelope;
                Text = $"You received a private message from '{message.Sender.Username}'. Click to read it!";
            }
        }

        public class MentionNotification : HighlightMessageNotification
        {
            public MentionNotification(Message message, Channel channel)
                : base(message, channel)
            {
                Icon = FontAwesome.Solid.At;
                Text = $"Your name was mentioned in chat by '{message.Sender.Username}'. Click to find out why!";
            }
        }

        public abstract class HighlightMessageNotification : SimpleNotification
        {
            protected HighlightMessageNotification(Message message, Channel channel)
            {
                this.message = message;
                this.channel = channel;
            }

            private readonly Message message;
            private readonly Channel channel;

            public override bool IsImportant => false;

            [BackgroundDependencyLoader]
            private void load(OsuColour colours, ChatOverlay chatOverlay, NotificationOverlay notificationOverlay)
            {
                IconBackground.Colour = colours.PurpleDark;

                Activated = delegate
                {
                    notificationOverlay.Hide();
                    chatOverlay.HighlightMessage(message, channel);
                    chatOverlay.Show();

                    return true;
                };
            }
        }
    }
}
