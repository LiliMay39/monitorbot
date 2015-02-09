﻿namespace scbot.slack
{
    public interface IBot
    {
        MessageResult Hello();
        MessageResult Unknown(string json);
        MessageResult Message(Message message);
    }
}