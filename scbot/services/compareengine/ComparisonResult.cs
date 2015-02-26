using scbot.bot;

namespace scbot.services.compareengine
{
    class ComparisonResult<T>
    {
        public readonly Response Response;
        public readonly T NewValue;

        public ComparisonResult(Response response, T newValue)
        {
            Response = response;
            NewValue = newValue;
        }
    }
}