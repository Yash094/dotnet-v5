﻿namespace Thirdweb
{
    public interface ITimeoutOptions
    {
        int GetTimeout(TimeoutType type, int fallback = Constants.DEFAULT_FETCH_TIMEOUT);
    }
}
