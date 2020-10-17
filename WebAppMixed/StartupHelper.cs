namespace WebAppMvc
{
    public static class StartupHelper
    {
        /// <summary>
        /// https://weblog.west-wind.com/posts/2018/Apr/12/Getting-the-NET-Core-Runtime-Version-in-a-Running-Application
        /// </summary>
        public static string GetNetVersion()
        {
            var framework =
                //Environment.Version.ToString();

                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            //Assembly
            //    .GetEntryAssembly()?
            //    .GetCustomAttribute<TargetFrameworkAttribute>()?
            //    .FrameworkName;

            return framework;
        }
    }
}
