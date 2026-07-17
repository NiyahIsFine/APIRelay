namespace APIRelay
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // 命名互斥锁保证整个系统中同时只运行一个实例，避免设置、日志等共享文件发生冲突。
            _instanceMutex = new Mutex(initiallyOwned: true,
                                       name: @"Global\APIRelay_Instance",
                                       createdNew: out bool createdNew);
            if (!createdNew)
            {
                _instanceMutex.Dispose();
                MessageBox.Show("APIRelay is already running.", "APIRelay",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.Run(new Form1());
            }
            finally
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
        }

        /// <summary>启动时若已有其他实例在运行则为 true(本进程是副本)。</summary>
        public static bool IsReplica { get; private set; }

        // 必须在整个生命周期内持有该句柄,否则信标会被提前释放,副本判定失效。
        private static Mutex? _instanceMutex;
    }
}