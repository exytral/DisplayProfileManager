namespace DisplayProfileManager.Common
{
    public static class TextFormat
    {
        public static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        public static string Plural(uint count, string noun) => Plural((int)count, noun);
    }
}
