﻿// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Data.SQLite;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ATI.ADL;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;



namespace GatelessGateSharp
{
    public partial class MainForm : Form
    {
        [DllImport("phymem_wrapper.dll")]
        public static extern int LoadPhyMemDriver();
        [DllImport("phymem_wrapper.dll")]
        public static extern void UnloadPhyMemDriver();

        private static MainForm instance;
        public static string shortAppName = "Gateless Gate Sharp";
        public static string appVersion = "1.1.4";
        public static string appName = shortAppName + " " + appVersion + " alpha";
        private static string databaseFileName = "GatelessGateSharp.sqlite";
        private static string logFileName = "GatelessGateSharp.log";
        private static string mAppStateFileName = "GatelessGateSharpState.txt";
        private static int mLaunchInterval = 1000;

        private Stratum mPrimaryStratum = null;
        private Stratum mSecondaryStratum = null;
        private List<Miner> mActiveMiners = new List<Miner>();
        private List<Miner> mInactiveMiners = new List<Miner>();
        private enum ApplicationGlobalState
        {
            Idle = 0,
            Mining = 1,
            Benchmarking = 2,
            Initializing = 3
        };
        private ApplicationGlobalState appState = ApplicationGlobalState.Initializing;

        private System.Threading.Mutex loggerMutex = new System.Threading.Mutex();
        private Control[] labelGPUVendorArray;
        private Control[] labelGPUNameArray;
        private Control[] labelGPUIDArray;
        private Control[] labelGPUSpeedArray;
        private Control[] labelGPUTempArray;
        private Control[] labelGPUActivityArray;
        private Control[] labelGPUFanArray;
        private Control[] labelGPUCoreClockArray;
        private Control[] labelGPUMemoryClockArray;
        private Control[] labelGPUSharesArray;
        private CheckBox[] checkBoxGPUEnableArray;
        private TabPage[] tabPageDeviceArray;
        private NumericUpDown[] numericUpDownDeviceEthashPascalThreadsArray;
        private NumericUpDown[] numericUpDownDeviceEthashPascalIntensityArray;
        private NumericUpDown[] numericUpDownDeviceEthashPascalPascalIterationsArray;
        private NumericUpDown[] numericUpDownDeviceEthashThreadsArray;
        private NumericUpDown[] numericUpDownDeviceEthashIntensityArray;
        private NumericUpDown[] numericUpDownDeviceEthashLocalWorkSizeArray;
        private NumericUpDown[] numericUpDownDevicePascalThreadsArray;
        private NumericUpDown[] numericUpDownDevicePascalIntensityArray;
        private NumericUpDown[] numericUpDownDevicePascalLocalWorkSizeArray;
        private NumericUpDown[] numericUpDownDeviceLbryThreadsArray;
        private NumericUpDown[] numericUpDownDeviceLbryIntensityArray;
        private NumericUpDown[] numericUpDownDeviceLbryLocalWorkSizeArray;
        private NumericUpDown[] numericUpDownDeviceCryptoNightThreadsArray;
        private NumericUpDown[] numericUpDownDeviceCryptoNightRawIntensityArray;
        private NumericUpDown[] numericUpDownDeviceCryptoNightLocalWorkSizeArray;

        private Device[] mDevices;
        private const int maxNumDevices = 8; // This depends on MainForm.
        private bool ADLInitialized = false;
        private bool NVMLInitialized = false;
        private int[] ADLAdapterIndexArray;
        private System.Threading.Mutex DeviceManagementLibrariesMutex = new System.Threading.Mutex();
        private ManagedCuda.Nvml.nvmlDevice[] nvmlDeviceArray;
        private bool phymemLoaded = false;

        private bool mDevFeeMode = true;
        private int mDevFeePercentage = 1;
        private int mDevFeeDurationInSeconds = 120;
        private string mDevFeeBitcoinAddress = "1k1WhysGsp7kNRy4atzzr6MaDrBiXw7wm";
        private string mDevFeeEthereumAddress = "0x91fa32e00b0f365d629fb625182a83fed61f0642";
        private string mDevFeeMoneroAddress = "463tWEBn5XZJSxLU6uLQnQ2iY9xuNcDbjLSjkn3XAXHCbLrTTErJrBWYgHJQyrCwkNgYvyV3z8zctJLPCZy24jvb3NiTcTJ.3c33141709b14b9bba1f1d49b39c69f8fb88a4cd571e4e80b3c0682375964a0f";
        private string mDevFeePascalAddress = "86646-64.b7db0252955d6b0f";
        private string mDevFeeLbryAddress = "bEFGDsEnfSzRs1UVKoUqaQfnvWAbPzLiuB";
        private string mDevFeeZcashAddress = "t1NwUDeSKu4BxkD58mtEYKDjzw5toiLfmCu";
        private string mDevFeeFeathercoinAddress = "6evDqvqep9WvRNnm2xV51bFZgwiw6kv7bh";
        private DateTime mDevFeeModeStartTime = DateTime.Now; // dummy

        private DateTime mStartTime = DateTime.Now;
        private string mCurrentPool = "NiceHash";

        public static MainForm Instance { get { return instance; } }
        public static bool DevFeeMode { get { return Instance.mDevFeeMode; } }

        private static string sLoggerBuffer = "";

        public static void Logger(string lines) {
            lines = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff") + " [" + System.Threading.Thread.CurrentThread.ManagedThreadId + "] " + lines + "\r\n";
            Console.Write(lines);
            try { Instance.loggerMutex.WaitOne(5000); } catch (Exception) { }
            sLoggerBuffer += lines;
            try { Instance.loggerMutex.ReleaseMutex(); } catch (Exception) { }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public static void UpdateLog() {
            try { Instance.loggerMutex.WaitOne(5000); } catch (Exception) { }
            var loggerBuffer = sLoggerBuffer;
            sLoggerBuffer = "";
            try { Instance.loggerMutex.ReleaseMutex(); } catch (Exception) { }

            if (loggerBuffer == "")
                return;

            try {
                using (var file = new System.IO.StreamWriter(logFileName, true)) {
                    file.Write(loggerBuffer);
                }

                Utilities.FixFPU();
                Instance.richTextBoxLog.SelectionLength = 0;
                Instance.richTextBoxLog.SelectionStart = Instance.richTextBoxLog.Text.Length;
                Instance.richTextBoxLog.ScrollToCaret();
                Instance.richTextBoxLog.Text += loggerBuffer;
                Instance.richTextBoxLog.SelectionLength = 0;
                Instance.richTextBoxLog.SelectionStart = Instance.richTextBoxLog.Text.Length;
                Instance.richTextBoxLog.ScrollToCaret();

                Instance.toolStripStatusLabel1.Text = loggerBuffer.Split('\n')[0].Replace("\r", "");
            } catch (Exception) { }
        }

        public MainForm() {
            instance = this;

            InitializeComponent();

            comboBoxCustomPool0Algorithm.SelectedIndex = 0;
            comboBoxCustomPool1Algorithm.SelectedIndex = 0;
            comboBoxCustomPool2Algorithm.SelectedIndex = 0;
            comboBoxCustomPool3Algorithm.SelectedIndex = 0;

            comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex = 0;
            comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex = 0;
            comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex = 0;
            comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex = 0;

            labelGPUVendorArray = new Control[] { labelGPU0Vendor, labelGPU1Vendor, labelGPU2Vendor, labelGPU3Vendor, labelGPU4Vendor, labelGPU5Vendor, labelGPU6Vendor, labelGPU7Vendor };
            labelGPUNameArray = new Control[] { labelGPU0Name, labelGPU1Name, labelGPU2Name, labelGPU3Name, labelGPU4Name, labelGPU5Name, labelGPU6Name, labelGPU7Name };
            labelGPUIDArray = new Control[] { labelGPU0ID, labelGPU1ID, labelGPU2ID, labelGPU3ID, labelGPU4ID, labelGPU5ID, labelGPU6ID, labelGPU7ID };
            labelGPUTempArray = new Control[] { labelGPU0Temp, labelGPU1Temp, labelGPU2Temp, labelGPU3Temp, labelGPU4Temp, labelGPU5Temp, labelGPU6Temp, labelGPU7Temp };
            labelGPUActivityArray = new Control[] { labelGPU0Activity, labelGPU1Activity, labelGPU2Activity, labelGPU3Activity, labelGPU4Activity, labelGPU5Activity, labelGPU6Activity, labelGPU7Activity };
            labelGPUFanArray = new Control[] { labelGPU0Fan, labelGPU1Fan, labelGPU2Fan, labelGPU3Fan, labelGPU4Fan, labelGPU5Fan, labelGPU6Fan, labelGPU7Fan };
            labelGPUSpeedArray = new Control[] { labelGPU0Speed, labelGPU1Speed, labelGPU2Speed, labelGPU3Speed, labelGPU4Speed, labelGPU5Speed, labelGPU6Speed, labelGPU7Speed };
            labelGPUCoreClockArray = new Control[] { labelGPU0CoreClock, labelGPU1CoreClock, labelGPU2CoreClock, labelGPU3CoreClock, labelGPU4CoreClock, labelGPU5CoreClock, labelGPU6CoreClock, labelGPU7CoreClock };
            labelGPUMemoryClockArray = new Control[] { labelGPU0MemoryClock, labelGPU1MemoryClock, labelGPU2MemoryClock, labelGPU3MemoryClock, labelGPU4MemoryClock, labelGPU5MemoryClock, labelGPU6MemoryClock, labelGPU7MemoryClock };
            labelGPUSharesArray = new Control[] { labelGPU0Shares, labelGPU1Shares, labelGPU2Shares, labelGPU3Shares, labelGPU4Shares, labelGPU5Shares, labelGPU6Shares, labelGPU7Shares };
            checkBoxGPUEnableArray = new CheckBox[] { checkBoxGPU0Enable, checkBoxGPU1Enable, checkBoxGPU2Enable, checkBoxGPU3Enable, checkBoxGPU4Enable, checkBoxGPU5Enable, checkBoxGPU6Enable, checkBoxGPU7Enable };
            tabPageDeviceArray = new TabPage[] { tabPageDevice0, tabPageDevice1, tabPageDevice2, tabPageDevice3, tabPageDevice4, tabPageDevice5, tabPageDevice6, tabPageDevice7 };
            numericUpDownDeviceEthashPascalThreadsArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashPascalThreads,
                numericUpDownDevice1EthashPascalThreads,
                numericUpDownDevice2EthashPascalThreads,
                numericUpDownDevice3EthashPascalThreads,
                numericUpDownDevice4EthashPascalThreads,
                numericUpDownDevice5EthashPascalThreads,
                numericUpDownDevice6EthashPascalThreads,
                numericUpDownDevice7EthashPascalThreads
            };
            numericUpDownDeviceEthashPascalIntensityArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashPascalIntensity,
                numericUpDownDevice1EthashPascalIntensity,
                numericUpDownDevice2EthashPascalIntensity,
                numericUpDownDevice4EthashPascalIntensity,
                numericUpDownDevice4EthashPascalIntensity,
                numericUpDownDevice5EthashPascalIntensity,
                numericUpDownDevice6EthashPascalIntensity,
                numericUpDownDevice7EthashPascalIntensity
            };
            numericUpDownDeviceEthashPascalPascalIterationsArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashPascalPascalIterations,
                numericUpDownDevice1EthashPascalPascalIterations,
                numericUpDownDevice2EthashPascalPascalIterations,
                numericUpDownDevice3EthashPascalPascalIterations,
                numericUpDownDevice4EthashPascalPascalIterations,
                numericUpDownDevice5EthashPascalPascalIterations,
                numericUpDownDevice6EthashPascalPascalIterations,
                numericUpDownDevice7EthashPascalPascalIterations
            };
            numericUpDownDeviceEthashThreadsArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashThreads,
                numericUpDownDevice1EthashThreads,
                numericUpDownDevice2EthashThreads,
                numericUpDownDevice3EthashThreads,
                numericUpDownDevice4EthashThreads,
                numericUpDownDevice5EthashThreads,
                numericUpDownDevice6EthashThreads,
                numericUpDownDevice7EthashThreads
            };
            numericUpDownDeviceEthashIntensityArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashIntensity,
                numericUpDownDevice1EthashIntensity,
                numericUpDownDevice2EthashIntensity,
                numericUpDownDevice3EthashIntensity,
                numericUpDownDevice4EthashIntensity,
                numericUpDownDevice5EthashIntensity,
                numericUpDownDevice6EthashIntensity,
                numericUpDownDevice7EthashIntensity
            };
            numericUpDownDeviceEthashLocalWorkSizeArray = new NumericUpDown[]
            {
                numericUpDownDevice0EthashLocalWorkSize,
                numericUpDownDevice1EthashLocalWorkSize,
                numericUpDownDevice2EthashLocalWorkSize,
                numericUpDownDevice3EthashLocalWorkSize,
                numericUpDownDevice4EthashLocalWorkSize,
                numericUpDownDevice5EthashLocalWorkSize,
                numericUpDownDevice6EthashLocalWorkSize,
                numericUpDownDevice7EthashLocalWorkSize
            };
            numericUpDownDeviceCryptoNightThreadsArray = new NumericUpDown[]
            {
                numericUpDownDevice0CryptoNightThreads,
                numericUpDownDevice1CryptoNightThreads,
                numericUpDownDevice2CryptoNightThreads,
                numericUpDownDevice3CryptoNightThreads,
                numericUpDownDevice4CryptoNightThreads,
                numericUpDownDevice5CryptoNightThreads,
                numericUpDownDevice6CryptoNightThreads,
                numericUpDownDevice7CryptoNightThreads
            };
            numericUpDownDeviceCryptoNightRawIntensityArray = new NumericUpDown[]
            {
                numericUpDownDevice0CryptoNightRawIntensity,
                numericUpDownDevice1CryptoNightRawIntensity,
                numericUpDownDevice2CryptoNightRawIntensity,
                numericUpDownDevice3CryptoNightRawIntensity,
                numericUpDownDevice4CryptoNightRawIntensity,
                numericUpDownDevice5CryptoNightRawIntensity,
                numericUpDownDevice6CryptoNightRawIntensity,
                numericUpDownDevice7CryptoNightRawIntensity
            };
            numericUpDownDeviceCryptoNightLocalWorkSizeArray = new NumericUpDown[]
            {
                numericUpDownDevice0CryptoNightLocalWorkSize,
                numericUpDownDevice1CryptoNightLocalWorkSize,
                numericUpDownDevice2CryptoNightLocalWorkSize,
                numericUpDownDevice3CryptoNightLocalWorkSize,
                numericUpDownDevice4CryptoNightLocalWorkSize,
                numericUpDownDevice5CryptoNightLocalWorkSize,
                numericUpDownDevice6CryptoNightLocalWorkSize,
                numericUpDownDevice7CryptoNightLocalWorkSize
            };
            numericUpDownDevicePascalThreadsArray = new NumericUpDown[]
            {
                numericUpDownDevice0PascalThreads,
                numericUpDownDevice1PascalThreads,
                numericUpDownDevice2PascalThreads,
                numericUpDownDevice3PascalThreads,
                numericUpDownDevice3PascalThreads,
                numericUpDownDevice5PascalThreads,
                numericUpDownDevice6PascalThreads,
                numericUpDownDevice7PascalThreads
            };
            numericUpDownDevicePascalIntensityArray = new NumericUpDown[]
            {
                numericUpDownDevice0PascalIntensity,
                numericUpDownDevice1PascalIntensity,
                numericUpDownDevice2PascalIntensity,
                numericUpDownDevice3PascalIntensity,
                numericUpDownDevice4PascalIntensity,
                numericUpDownDevice5PascalIntensity,
                numericUpDownDevice6PascalIntensity,
                numericUpDownDevice7PascalIntensity
            };
            numericUpDownDevicePascalLocalWorkSizeArray = new NumericUpDown[]
            {
                numericUpDownDevice0PascalLocalWorkSize,
                numericUpDownDevice1PascalLocalWorkSize,
                numericUpDownDevice2PascalLocalWorkSize,
                numericUpDownDevice3PascalLocalWorkSize,
                numericUpDownDevice4PascalLocalWorkSize,
                numericUpDownDevice5PascalLocalWorkSize,
                numericUpDownDevice6PascalLocalWorkSize,
                numericUpDownDevice7PascalLocalWorkSize
            };
            numericUpDownDeviceLbryThreadsArray = new NumericUpDown[]
            {
                numericUpDownDevice0LbryThreads,
                numericUpDownDevice1LbryThreads,
                numericUpDownDevice2LbryThreads,
                numericUpDownDevice3LbryThreads,
                numericUpDownDevice4LbryThreads,
                numericUpDownDevice5LbryThreads,
                numericUpDownDevice6LbryThreads,
                numericUpDownDevice7LbryThreads
            };
            numericUpDownDeviceLbryIntensityArray = new NumericUpDown[]
            {
                numericUpDownDevice0LbryIntensity,
                numericUpDownDevice1LbryIntensity,
                numericUpDownDevice2LbryIntensity,
                numericUpDownDevice3LbryIntensity,
                numericUpDownDevice4LbryIntensity,
                numericUpDownDevice5LbryIntensity,
                numericUpDownDevice6LbryIntensity,
                numericUpDownDevice7LbryIntensity
            };
            numericUpDownDeviceLbryLocalWorkSizeArray = new NumericUpDown[]
            {
                numericUpDownDevice0LbryLocalWorkSize,
                numericUpDownDevice1LbryLocalWorkSize,
                numericUpDownDevice2LbryLocalWorkSize,
                numericUpDownDevice3LbryLocalWorkSize,
                numericUpDownDevice4LbryLocalWorkSize,
                numericUpDownDevice5LbryLocalWorkSize,
                numericUpDownDevice6LbryLocalWorkSize,
                numericUpDownDevice6LbryLocalWorkSize
            };
        }

        private void CreateNewDatabase() {
            SQLiteConnection.CreateFile(databaseFileName);
            using (var conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;")) {
                conn.Open();
                var sql = "create table wallet_addresses (coin varchar(1024), address varchar(1024));";
                using (var command = new SQLiteCommand(sql, conn)) { command.ExecuteNonQuery(); }

                sql = "create table pools (name varchar(1024));";
                using (var command = new SQLiteCommand(sql, conn)) { command.ExecuteNonQuery(); }

                sql = "create table properties (name varchar(1024), value varchar(1024));";
                using (var command = new SQLiteCommand(sql, conn)) { command.ExecuteNonQuery(); }

                sql = "create table device_parameters (device_id int, device_vendor varchar(1024), device_name varchar(1024), parameter_name varchar(1024), parameter_value varchar(1024));";
                using (var command = new SQLiteCommand(sql, conn)) { command.ExecuteNonQuery(); }
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void LoadDatabase() {
            try {
                using (var conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;")) {
                    conn.Open();
                    var sql = "select * from wallet_addresses";
                    using (var command = new SQLiteCommand(sql, conn)) {
                        using (var reader = command.ExecuteReader()) {

                            while (reader.Read())
                                if ((string)reader["coin"] == "bitcoin")
                                    textBoxBitcoinAddress.Text = (string)reader["address"];
                                else if ((string)reader["coin"] == "ethereum")
                                    textBoxEthereumAddress.Text = (string)reader["address"];
                                else if ((string)reader["coin"] == "monero")
                                    textBoxMoneroAddress.Text = (string)reader["address"];
                                else if ((string)reader["coin"] == "zcash")
                                    textBoxZcashAddress.Text = (string)reader["address"];
                                else if ((string)reader["coin"] == "pascal")
                                    textBoxPascalAddress.Text = (string)reader["address"];
                                else if ((string)reader["coin"] == "lbry")
                                    textBoxLbryAddress.Text = (string)reader["address"];
                        }
                    }

                    try {
                        sql = "select * from pools";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            using (var reader = command.ExecuteReader()) {
                                var oldItems = new List<string>();
                                foreach (string poolName in listBoxPoolPriorities.Items)
                                    oldItems.Add(poolName);
                                listBoxPoolPriorities.Items.Clear();
                                while (reader.Read())
                                    listBoxPoolPriorities.Items.Add((string)reader["name"]);
                                foreach (var poolName in oldItems)
                                    if (!listBoxPoolPriorities.Items.Contains(poolName))
                                        listBoxPoolPriorities.Items.Add(poolName);
                            }
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                    }

                    try {
                        sql = "select * from properties";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            using (var reader = command.ExecuteReader()) {
                                while (reader.Read()) {
                                    var propertyName = (string)reader["name"];
                                    if (propertyName == "coin_to_mine") {
                                        var coinToMine = (string)reader["value"];
                                        if (coinToMine == "ethereum") {
                                            radioButtonEthereum.Checked = true;
                                        } else if (coinToMine == "ethereum_pascal") {
                                            radioButtonEthereumPascal.Checked = true;
                                        } else if (coinToMine == "monero") {
                                            radioButtonMonero.Checked = true;
                                        } else if (coinToMine == "zcash") {
                                            radioButtonZcash.Checked = true;
                                        } else if (coinToMine == "lbry") {
                                            radioButtonLbry.Checked = true;
                                        } else if (coinToMine == "pascal") {
                                            radioButtonPascal.Checked = true;
                                        } else {
                                            radioButtonEthereum.Checked = true;
                                        }
                                    } else if (propertyName == "pool_rig_id") {
                                        textBoxRigID.Text = (string)reader["value"];
                                    } else if (propertyName == "pool_email") {
                                        textBoxEmail.Text = (string)reader["value"];
                                    } else if (propertyName == "auto_start") {
                                        checkBoxAutoStart.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "launch_at_startup") {
                                        checkBoxLaunchAtStartup.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu0") {
                                        checkBoxGPU0Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu1") {
                                        checkBoxGPU1Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu2") {
                                        checkBoxGPU2Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu3") {
                                        checkBoxGPU3Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu4") {
                                        checkBoxGPU4Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu5") {
                                        checkBoxGPU5Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu6") {
                                        checkBoxGPU6Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_gpu7") {
                                        checkBoxGPU7Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "enable_phymem") {
                                        checkBoxEnablePhymem.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "custom_pool0_enabled") {
                                        checkBoxCustomPool0Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "custom_pool1_enabled") {
                                        checkBoxCustomPool1Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "custom_pool2_enabled") {
                                        checkBoxCustomPool2Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "custom_pool3_enabled") {
                                        checkBoxCustomPool3Enable.Checked = (string)reader["value"] == "true";
                                    } else if (propertyName == "custom_pool0_host") {
                                        textBoxCustomPool0Host.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_login") {
                                        textBoxCustomPool0Login.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_password") {
                                        textBoxCustomPool0Password.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_host") {
                                        textBoxCustomPool1Host.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_login") {
                                        textBoxCustomPool1Login.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_password") {
                                        textBoxCustomPool1Password.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_host") {
                                        textBoxCustomPool2Host.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_login") {
                                        textBoxCustomPool2Login.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_password") {
                                        textBoxCustomPool2Password.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_host") {
                                        textBoxCustomPool3Host.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_login") {
                                        textBoxCustomPool3Login.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_password") {
                                        textBoxCustomPool3Password.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_port") {
                                        numericUpDownCustomPool0Port.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool1_port") {
                                        numericUpDownCustomPool1Port.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool2_port") {
                                        numericUpDownCustomPool2Port.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool3_port") {
                                        numericUpDownCustomPool3Port.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool0_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool0Algorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool0Algorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool0Algorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool1_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool1Algorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool1Algorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool1Algorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool2_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool2Algorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool2Algorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool2Algorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool3_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool3Algorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool3Algorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool3Algorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool0_secondary_host") {
                                        textBoxCustomPool0SecondaryHost.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_secondary_login") {
                                        textBoxCustomPool0SecondaryLogin.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_secondary_password") {
                                        textBoxCustomPool0SecondaryPassword.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_secondary_host") {
                                        textBoxCustomPool1SecondaryHost.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_secondary_login") {
                                        textBoxCustomPool1SecondaryLogin.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool1_secondary_password") {
                                        textBoxCustomPool1SecondaryPassword.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_secondary_host") {
                                        textBoxCustomPool2SecondaryHost.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_secondary_login") {
                                        textBoxCustomPool2SecondaryLogin.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool2_secondary_password") {
                                        textBoxCustomPool2SecondaryPassword.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_secondary_host") {
                                        textBoxCustomPool3SecondaryHost.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_secondary_login") {
                                        textBoxCustomPool3SecondaryLogin.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool3_secondary_password") {
                                        textBoxCustomPool3SecondaryPassword.Text = (string)reader["value"];
                                    } else if (propertyName == "custom_pool0_secondary_port") {
                                        numericUpDownCustomPool0SecondaryPort.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool1_secondary_port") {
                                        numericUpDownCustomPool1SecondaryPort.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool2_secondary_port") {
                                        numericUpDownCustomPool2SecondaryPort.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool3_secondary_port") {
                                        numericUpDownCustomPool3SecondaryPort.Value = decimal.Parse((string)reader["value"]);
                                    } else if (propertyName == "custom_pool0_secondary_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool0SecondaryAlgorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool0SecondaryAlgorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool1_secondary_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool1SecondaryAlgorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool1SecondaryAlgorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool2_secondary_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool2SecondaryAlgorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool2SecondaryAlgorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "custom_pool3_secondary_algorithm") {
                                        for (int i = 0; i < comboBoxCustomPool3SecondaryAlgorithm.Items.Count; ++i) {
                                            var item = comboBoxCustomPool3SecondaryAlgorithm.Items[i];
                                            if ((string)item == (string)reader["value"])
                                                comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex = i;
                                        }

                                    } else if (propertyName == "disable_auto_start_prompt") {
                                        checkBoxDisableAutoStartPrompt.Checked = (string)reader["value"] == "true";
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                    }

                    try {
                        sql = "select * from device_parameters";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            using (var reader = command.ExecuteReader()) {
                                while (reader.Read()) {
                                    var deviceID = (int)reader["device_id"];
                                    var deviceVendor = (string)reader["device_vendor"];
                                    var deviceName = (string)reader["device_name"];
                                    var name = (string)reader["parameter_name"];
                                    var value = (string)reader["parameter_value"];
                                    if (deviceID >= mDevices.Length || deviceVendor != mDevices[deviceID].Vendor ||
                                        deviceName != mDevices[deviceID].Name)
                                        continue;
                                    if (name == "ethash_pascal_threads")
                                        numericUpDownDeviceEthashPascalThreadsArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "ethash_pascal_intensity")
                                        numericUpDownDeviceEthashPascalIntensityArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "ethash_pascal_pascal_iterations")
                                        numericUpDownDeviceEthashPascalPascalIterationsArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "lbry_threads")
                                        numericUpDownDeviceLbryThreadsArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "lbry_intensity")
                                        numericUpDownDeviceLbryIntensityArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "lbry_local_work_size")
                                        numericUpDownDeviceLbryLocalWorkSizeArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "pascal_threads")
                                        numericUpDownDevicePascalThreadsArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "pascal_intensity")
                                        numericUpDownDevicePascalIntensityArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "pascal_local_work_size")
                                        numericUpDownDevicePascalLocalWorkSizeArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "ethash_threads")
                                        numericUpDownDeviceEthashThreadsArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "ethash_intensity")
                                        numericUpDownDeviceEthashIntensityArray[deviceID].Value = decimal.Parse(value);
                                    else if (name == "ethash_local_work_size")
                                        numericUpDownDeviceEthashLocalWorkSizeArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "cryptonight_threads")
                                        numericUpDownDeviceCryptoNightThreadsArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "cryptonight_intensity")
                                        numericUpDownDeviceCryptoNightRawIntensityArray[deviceID].Value =
                                            decimal.Parse(value) * mDevices[deviceID].MaxComputeUnits;
                                    else if (name == "cryptonight_raw_intensity")
                                        numericUpDownDeviceCryptoNightRawIntensityArray[deviceID].Value =
                                            decimal.Parse(value);
                                    else if (name == "cryptonight_local_work_size")
                                        numericUpDownDeviceCryptoNightLocalWorkSizeArray[deviceID].Value =
                                            decimal.Parse(value);
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                    }

                    conn.Close();
                }
            } catch (Exception ex) {
                Logger("Exception: " + ex.Message + ex.StackTrace);
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void UpdateDatabase() {
            try {
                using (var conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;")) {
                    conn.Open();
                    var sql = "delete from wallet_addresses";
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.ExecuteNonQuery();
                    }

                    sql = "insert into wallet_addresses (coin, address) values (@coin, @address)";
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "bitcoin");
                        command.Parameters.AddWithValue("@address", textBoxBitcoinAddress.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "ethereum");
                        command.Parameters.AddWithValue("@address", textBoxEthereumAddress.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "monero");
                        command.Parameters.AddWithValue("@address", textBoxMoneroAddress.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "zcash");
                        command.Parameters.AddWithValue("@address", textBoxZcashAddress.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "pascal");
                        command.Parameters.AddWithValue("@address", textBoxPascalAddress.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@coin", "lbry");
                        command.Parameters.AddWithValue("@address", textBoxLbryAddress.Text);
                        command.ExecuteNonQuery();
                    }

                    try {
                        sql = "delete from pools";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                        sql = "create table pools (name varchar(1024));";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    }

                    sql = "insert into pools (name) values (@name)";
                    foreach (string poolName in listBoxPoolPriorities.Items)
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@name", poolName);
                            command.ExecuteNonQuery();
                        }

                    try {
                        sql = "delete from properties";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                        sql = "create table properties (name varchar(1024), value varchar(1024));";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    }

                    sql = "insert into properties (name, value) values (@name, @value)";
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "coin_to_mine");
                        command.Parameters.AddWithValue("@value",
                                                        radioButtonEthereum.Checked ? "ethereum" :
                                                        radioButtonEthereumPascal.Checked ? "ethereum_pascal" :
                                                        radioButtonMonero.Checked ? "monero" :
                                                        radioButtonZcash.Checked ? "zcash" :
                                                        radioButtonLbry.Checked ? "lbry" :
                                                        radioButtonPascal.Checked ? "pascal" :
                                                                                        "most_profitable");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "pool_rig_id");
                        command.Parameters.AddWithValue("@value", textBoxRigID.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "pool_email");
                        command.Parameters.AddWithValue("@value", textBoxEmail.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "auto_start");
                        command.Parameters.AddWithValue("@value", checkBoxAutoStart.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "launch_at_startup");
                        command.Parameters.AddWithValue("@value", checkBoxLaunchAtStartup.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "disable_auto_start_prompt");
                        command.Parameters.AddWithValue("@value", checkBoxDisableAutoStartPrompt.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }

                    for (var i = 0; i < mDevices.Length; ++i)
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@name", "enable_gpu" + i);
                            command.Parameters.AddWithValue("@value", checkBoxGPUEnableArray[i].Checked ? "true" : "false");
                            command.ExecuteNonQuery();
                        }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0Host.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0Login.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0Password.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1Host.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1Login.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1Password.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2Host.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2Login.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2Password.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3Host.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3Login.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3Password.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_enabled");
                        command.Parameters.AddWithValue("@value", checkBoxCustomPool0Enable.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_enabled");
                        command.Parameters.AddWithValue("@value", checkBoxCustomPool1Enable.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_enabled");
                        command.Parameters.AddWithValue("@value", checkBoxCustomPool2Enable.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_enabled");
                        command.Parameters.AddWithValue("@value", checkBoxCustomPool3Enable.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool0Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool1Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool2Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool3Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool0Algorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool1Algorithm.Items[comboBoxCustomPool1Algorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool2Algorithm.Items[comboBoxCustomPool2Algorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool3Algorithm.Items[comboBoxCustomPool3Algorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_secondary_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0SecondaryHost.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_secondary_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0SecondaryLogin.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_secondary_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool0SecondaryPassword.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_secondary_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1SecondaryHost.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_secondary_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1SecondaryLogin.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_secondary_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool1SecondaryPassword.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_secondary_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2SecondaryHost.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_secondary_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2SecondaryLogin.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_secondary_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool2SecondaryPassword.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_secondary_host");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3SecondaryHost.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_secondary_login");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3SecondaryLogin.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_secondary_password");
                        command.Parameters.AddWithValue("@value", textBoxCustomPool3SecondaryPassword.Text);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_secondary_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool0SecondaryPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_secondary_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool1SecondaryPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_secondary_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool2SecondaryPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_secondary_port");
                        command.Parameters.AddWithValue("@value", numericUpDownCustomPool3SecondaryPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool0_secondary_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool0SecondaryAlgorithm.Items[comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool1_secondary_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool1SecondaryAlgorithm.Items[comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool2_secondary_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool2SecondaryAlgorithm.Items[comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "custom_pool3_secondary_algorithm");
                        command.Parameters.AddWithValue("@value", (string)comboBoxCustomPool3SecondaryAlgorithm.Items[comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex]);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "auto_start");
                        command.Parameters.AddWithValue("@value", checkBoxAutoStart.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "launch_at_startup");
                        command.Parameters.AddWithValue("@value", checkBoxLaunchAtStartup.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand(sql, conn)) {
                        command.Parameters.AddWithValue("@name", "enable_phymem");
                        command.Parameters.AddWithValue("@value", checkBoxEnablePhymem.Checked ? "true" : "false");
                        command.ExecuteNonQuery();
                    }

                    for (var i = 0; i < mDevices.Length; ++i)
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@name", "enable_gpu" + i);
                            command.Parameters.AddWithValue("@value", checkBoxGPUEnableArray[i].Checked ? "true" : "false");
                            command.ExecuteNonQuery();
                        }

                    try {
                        sql = "delete from device_parameters";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    } catch (Exception ex) {
                        Logger("Exception: " + ex.Message + ex.StackTrace);
                        sql = "create table device_parameters (device_id int, device_vendor varchar(1024), device_name varchar(1024), parameter_name varchar(1024), parameter_value varchar(1024));";
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.ExecuteNonQuery();
                        }
                    }

                    sql = "insert into device_parameters (device_id, device_vendor, device_name, parameter_name, parameter_value) values (@device_id, @device_vendor, @device_name, @parameter_name, @parameter_value)";
                    for (var i = 0; i < mDevices.Length; ++i) {
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_pascal_threads");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashPascalThreadsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_pascal_intensity");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashPascalIntensityArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_pascal_pascal_iterations");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashPascalPascalIterationsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_threads");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashThreadsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_intensity");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashIntensityArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "ethash_local_work_size");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceEthashLocalWorkSizeArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "lbry_threads");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceLbryThreadsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "lbry_intensity");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceLbryIntensityArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "lbry_local_work_size");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceLbryLocalWorkSizeArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "pascal_threads");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDevicePascalThreadsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "pascal_intensity");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDevicePascalIntensityArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "pascal_local_work_size");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDevicePascalLocalWorkSizeArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "cryptonight_threads");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceCryptoNightThreadsArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "cryptonight_raw_intensity");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceCryptoNightRawIntensityArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                        using (var command = new SQLiteCommand(sql, conn)) {
                            command.Parameters.AddWithValue("@device_id", i);
                            command.Parameters.AddWithValue("@device_vendor", mDevices[i].Vendor);
                            command.Parameters.AddWithValue("@device_name", mDevices[i].Name);
                            command.Parameters.AddWithValue("@parameter_name", "cryptonight_local_work_size");
                            command.Parameters.AddWithValue("@parameter_value", numericUpDownDeviceCryptoNightLocalWorkSizeArray[i].Value.ToString());
                            command.ExecuteNonQuery();
                        }
                    }
                }
            } catch (Exception ex) {
                Logger("Exception in UpdateDatabase(): " + ex.Message + ex.StackTrace);
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void MainForm_Load(object sender, EventArgs e) {
            Logger(appName + " started.");

            InitializeDevices();
            if (!System.IO.File.Exists(databaseFileName))
                CreateNewDatabase();
            LoadDatabase();

            if (checkBoxEnablePhymem.Checked) {
                if (LoadPhyMemDriver() != 0) {
                    Logger("Successfully loaded phymem.");
                    phymemLoaded = true;
                } else {
                    Logger("Failed to load phymem.");
                    var w = new Form() { Size = new System.Drawing.Size(0, 0) };
                    Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith((t) => w.Close(),
                        TaskScheduler.FromCurrentSynchronizationContext());
                    w.BringToFront();
                    MessageBox.Show(w, "Failed to load phymem.", "Gateless Gate Sharp", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
            }

            Text = appName; // Set the window title.

            // Do everything to turn off TDR.
            foreach (var controlSet in new string[] { "CurrentControlSet", "ControlSet001" })
                // This shouldn't be necessary but it doesn't work without this.
                foreach (var path in new string[] {
                @"HKEY_LOCAL_MACHINE\System\" + controlSet + @"\Control\GraphicsDrivers",
                @"HKEY_LOCAL_MACHINE\System\" + controlSet + @"\Control\GraphicsDrivers\TdrWatch"
            }) {
                    try { Microsoft.Win32.Registry.SetValue(path, "TdrLevel", 0); } catch (Exception) { }
                    try { Microsoft.Win32.Registry.SetValue(path, "TdrDelay", 60); } catch (Exception) { }
                    try { Microsoft.Win32.Registry.SetValue(path, "TdrDdiDelay", 60); } catch (Exception) { }
                    try { Microsoft.Win32.Registry.SetValue(path, "TdrLimitTime", 60); } catch (Exception) { }
                    try { Microsoft.Win32.Registry.SetValue(path, "TdrLimitCount", 256); } catch (Exception) { }
                    try { Microsoft.Win32.Registry.SetValue(path, "TDR_RECOVERY", 0); } catch (Exception) { } // Undocumented but found on Windows 10.
                }

            appState = ApplicationGlobalState.Idle;
            UpdateControls();

            // Auto-start mining if necessary.
            var autoStart = checkBoxAutoStart.Checked;
            try {
                if (System.IO.File.ReadAllLines(mAppStateFileName)[0] == "Mining")
                    autoStart = true;
            } catch (Exception) { }
            if (autoStart) {
                if (checkBoxDisableAutoStartPrompt.Checked
                    || MessageBox.Show(Utilities.GetAutoClosingForm(), "Mining will start automatically in 10 seconds.",
                        "Gateless Gate Sharp", MessageBoxButtons.OKCancel) != DialogResult.Cancel) {
                    timerAutoStart.Enabled = true;
                } else {
                    try { using (var file = new System.IO.StreamWriter(mAppStateFileName, false)) file.WriteLine("Idle"); } catch (Exception) { }
                }
            }

            //Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void InitializeDevices() {
            mDevices = Device.GetAllDevices();
            if (mDevices.Length > maxNumDevices)
                Array.Resize<Device>(ref mDevices, maxNumDevices);
            Logger("Number of Devices: " + mDevices.Length);

            foreach (var device in mDevices) {
                var openclDevice = device.GetComputeDevice();
                var index = device.DeviceIndex;
                labelGPUVendorArray[index].Text = device.Vendor;
                labelGPUNameArray[index].Text = openclDevice.Name;

                labelGPUSpeedArray[index].Text = "-";
                labelGPUActivityArray[index].Text = "-";
                labelGPUTempArray[index].Text = "-";
                labelGPUFanArray[index].Text = "-";
                labelGPUSharesArray[index].Text = "-";
            }

            for (var index = maxNumDevices - 1; index >= mDevices.Length; --index) {
                labelGPUVendorArray[index].Visible = false;
                labelGPUNameArray[index].Visible = false;
                labelGPUIDArray[index].Visible = false;
                labelGPUSpeedArray[index].Visible = false;
                labelGPUActivityArray[index].Visible = false;
                labelGPUTempArray[index].Visible = false;
                labelGPUFanArray[index].Visible = false;
                labelGPUCoreClockArray[index].Visible = false;
                labelGPUMemoryClockArray[index].Visible = false;
                labelGPUSharesArray[index].Visible = false;
                checkBoxGPUEnableArray[index].Visible = false;
            }

            for (var index = maxNumDevices - 1; index >= mDevices.Length; --index)
                tabControlDeviceSettings.TabPages.RemoveAt(index);

            for (var index = maxNumDevices - 1; index >= mDevices.Length; --index) {
                Array.Resize(ref labelGPUVendorArray, mDevices.Length);
                Array.Resize(ref labelGPUNameArray, mDevices.Length);
                Array.Resize(ref labelGPUIDArray, mDevices.Length);
                Array.Resize(ref labelGPUSpeedArray, mDevices.Length);
                Array.Resize(ref labelGPUActivityArray, mDevices.Length);
                Array.Resize(ref labelGPUTempArray, mDevices.Length);
                Array.Resize(ref labelGPUFanArray, mDevices.Length);
                Array.Resize(ref labelGPUCoreClockArray, mDevices.Length);
                Array.Resize(ref labelGPUMemoryClockArray, mDevices.Length);
                Array.Resize(ref labelGPUSharesArray, mDevices.Length);
                Array.Resize(ref checkBoxGPUEnableArray, mDevices.Length);

                Array.Resize(ref tabPageDeviceArray, mDevices.Length);
                Array.Resize(ref numericUpDownDeviceEthashIntensityArray, mDevices.Length);
                Array.Resize(ref numericUpDownDeviceCryptoNightThreadsArray, mDevices.Length);
                Array.Resize(ref numericUpDownDeviceCryptoNightRawIntensityArray, mDevices.Length);
                Array.Resize(ref numericUpDownDeviceCryptoNightLocalWorkSizeArray, mDevices.Length);
            }

            var ADLRet = -1;
            var NumberOfAdapters = 0;
            ADLAdapterIndexArray = new int[mDevices.Length];
            for (var i = 0; i < mDevices.Length; i++)
                ADLAdapterIndexArray[i] = -1;
            if (null != ADL.ADL_Main_Control_Create)
                ADLRet = ADL.ADL_Main_Control_Create(ADL.ADL_Main_Memory_Alloc, 1);
            if (ADL.ADL_SUCCESS == ADLRet) {
                Logger("Successfully initialized AMD Display Library.");
                ADLInitialized = true;
                if (null != ADL.ADL_Adapter_NumberOfAdapters_Get)
                    ADL.ADL_Adapter_NumberOfAdapters_Get(ref NumberOfAdapters);
                Logger("Number of ADL Adapters: " + NumberOfAdapters.ToString());

                if (0 < NumberOfAdapters) {
                    ADLAdapterInfoArray OSAdapterInfoData;
                    OSAdapterInfoData = new ADLAdapterInfoArray();

                    if (null != ADL.ADL_Adapter_AdapterInfo_Get) {
                        var AdapterBuffer = IntPtr.Zero;
                        var size = Marshal.SizeOf(OSAdapterInfoData);
                        AdapterBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSAdapterInfoData, AdapterBuffer, false);

                        if (null != ADL.ADL_Adapter_AdapterInfo_Get) {
                            ADLRet = ADL.ADL_Adapter_AdapterInfo_Get(AdapterBuffer, size);
                            if (ADL.ADL_SUCCESS == ADLRet) {
                                OSAdapterInfoData = (ADLAdapterInfoArray)Marshal.PtrToStructure(AdapterBuffer, OSAdapterInfoData.GetType());
                                var IsActive = 0;

                                //int deviceIndex = 0;
                                int deviceIndex = 0;
                                foreach (var device in mDevices) {
                                    var openclDevice = device.GetComputeDevice();
                                    if (openclDevice.Vendor == "Advanced Micro Devices, Inc.")
                                        for (var i = 0; i < NumberOfAdapters; i++) {
                                            if (null != ADL.ADL_Adapter_Active_Get)
                                                ADLRet = ADL.ADL_Adapter_Active_Get(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref IsActive);
                                            if (OSAdapterInfoData.ADLAdapterInfo[i].BusNumber == openclDevice.PciBusIdAMD
                                                && (ADLAdapterIndexArray[deviceIndex] < 0 || IsActive != 0)) {
                                                ADLAdapterIndexArray[deviceIndex] = OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex;
                                                device.SetADLName(OSAdapterInfoData.ADLAdapterInfo[i].AdapterName);
                                                labelGPUNameArray[deviceIndex].Text = device.Name;
                                            }
                                        }
                                    ++deviceIndex;
                                }
                            } else {
                                Logger("ADL_Adapter_AdapterInfo_Get() returned error code " + ADLRet.ToString());
                            }
                        }
                        // Release the memory for the AdapterInfo structure
                        if (IntPtr.Zero != AdapterBuffer)
                            Marshal.FreeCoTaskMem(AdapterBuffer);
                    }
                }
            } else {
                Logger("Failed to initialize AMD Display Library.");
            }

            try {
                if (ManagedCuda.Nvml.NvmlNativeMethods.nvmlInit() == 0) {
                    Logger("Successfully initialized NVIDIA Management Library.");
                    uint nvmlDeviceCount = 0;
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetCount(ref nvmlDeviceCount);
                    Logger("NVML Device Count: " + nvmlDeviceCount);

                    nvmlDeviceArray = new ManagedCuda.Nvml.nvmlDevice[mDevices.Length];
                    for (uint i = 0; i < nvmlDeviceCount; ++i) {
                        var nvmlDevice = new ManagedCuda.Nvml.nvmlDevice();
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetHandleByIndex(i, ref nvmlDevice);
                        var info = new ManagedCuda.Nvml.nvmlPciInfo();
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetPciInfo(nvmlDevice, ref info);

                        uint j;
                        for (j = 0; j < mDevices.Length; ++j)
                            if (mDevices[j].GetComputeDevice().Vendor == "NVIDIA Corporation" && mDevices[j].GetComputeDevice().PciBusIdNV == info.bus) {
                                nvmlDeviceArray[j] = nvmlDevice;
                                break;
                            }
                        if (j >= mDevices.Length)
                            throw new Exception();
                    }

                    NVMLInitialized = true;
                }
            } catch (Exception) {
            }
            if (!NVMLInitialized) {
                Logger("Failed to initialize NVIDIA Management Library.");
            } else {
            }

            foreach (var device in mDevices) {
                tabPageDeviceArray[device.DeviceIndex].Text = "#" + device.DeviceIndex + ": " + device.Vendor + " " + device.Name;

                // EthashPascal
                numericUpDownDeviceEthashPascalThreadsArray[device.DeviceIndex].Value = (decimal)1;
                numericUpDownDeviceEthashPascalIntensityArray[device.DeviceIndex].Value = (decimal)2000;
                numericUpDownDeviceEthashPascalPascalIterationsArray[device.DeviceIndex].Maximum = (decimal)(device.Vendor == "NVIDIA" ? 4 : 4);

                // Ethash
                numericUpDownDeviceEthashThreadsArray[device.DeviceIndex].Value = (decimal)1;
                numericUpDownDeviceEthashIntensityArray[device.DeviceIndex].Value = (decimal)2000;
                numericUpDownDeviceEthashLocalWorkSizeArray[device.DeviceIndex].Maximum = (decimal)(device.Vendor == "NVIDIA" ? 512 : 256);
                numericUpDownDeviceEthashLocalWorkSizeArray[device.DeviceIndex].Value = (decimal)(device.Vendor == "NVIDIA" ? 192 : 192);

                // Lbry
                numericUpDownDeviceLbryThreadsArray[device.DeviceIndex].Value = (decimal)1;
                numericUpDownDeviceLbryIntensityArray[device.DeviceIndex].Value = (decimal)8192;
                numericUpDownDeviceLbryLocalWorkSizeArray[device.DeviceIndex].Maximum = (decimal)(device.Vendor == "NVIDIA" ? 512 : 256);
                numericUpDownDeviceLbryLocalWorkSizeArray[device.DeviceIndex].Value = (decimal)(device.Vendor == "NVIDIA" ? 32 : 64);

                // Pasacal
                numericUpDownDevicePascalThreadsArray[device.DeviceIndex].Value = (decimal)2;
                numericUpDownDevicePascalIntensityArray[device.DeviceIndex].Value = (decimal)8192;
                numericUpDownDevicePascalLocalWorkSizeArray[device.DeviceIndex].Maximum = (decimal)(device.Vendor == "NVIDIA" ? 512 : 256);
                numericUpDownDevicePascalLocalWorkSizeArray[device.DeviceIndex].Value = (decimal)(device.Vendor == "NVIDIA" ? 256 : 256);

                // CryptoNight
                numericUpDownDeviceCryptoNightThreadsArray[device.DeviceIndex].Value = (decimal)(device.Vendor == "AMD" ? 2 : 1);
                numericUpDownDeviceCryptoNightLocalWorkSizeArray[device.DeviceIndex].Value = (decimal)(device.Vendor == "AMD" ? 8 : 4);
                numericUpDownDeviceCryptoNightRawIntensityArray[device.DeviceIndex].Value
                    = (decimal)(device.Vendor == "AMD" && device.Name == "Radeon RX 470" ? 24 * device.MaxComputeUnits :
                                device.Vendor == "AMD" && device.Name == "Radeon RX 570" ? 24 * device.MaxComputeUnits :
                                device.Vendor == "AMD" && device.Name == "Radeon RX 480" ? 28 * device.MaxComputeUnits :
                                device.Vendor == "AMD" && device.Name == "Radeon RX 580" ? 28 * device.MaxComputeUnits :
                                device.Vendor == "AMD" && device.Name == "Radeon R9 Fury X/Nano" ? 14 * device.MaxComputeUnits :
                                device.Vendor == "AMD" ? 16 * device.MaxComputeUnits :
                                device.Vendor == "NVIDIA" && device.Name == "GeForce GTX 1080 Ti" ? 32 * device.MaxComputeUnits :
                                                                                                    16 * device.MaxComputeUnits);
            }

            UpdateStatsWithShortPolling();
            timerDeviceStatusUpdates.Enabled = true;
            UpdateStatsWithLongPolling();
            timerCurrencyStatUpdates.Enabled = true;
        }

        private class CustomWebClient : System.Net.WebClient
        {
            protected override System.Net.WebRequest GetWebRequest(Uri uri) {
                Encoding = System.Text.Encoding.UTF8;
                var request = base.GetWebRequest(uri);
                request.Timeout = 10 * 1000;
                return request;
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void UpdateStatsWithLongPolling() {
            try {
                double totalSpeed = 0;
                foreach (var miner in mActiveMiners)
                    totalSpeed += miner.Speed;
                double secondaryTotalSpeed = 0;
                foreach (var miner in mActiveMiners)
                    secondaryTotalSpeed += miner.SecondSpeed;

                var client = new CustomWebClient();
                double USDBTC = 0;
                {
                    var jsonString = client.DownloadString("https://blockchain.info/ticker");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var USD = (JContainer)response["USD"];
                    USDBTC = (double)USD["15m"];
                }

                var USDETH = 0.0;
                var USDXMR = 0.0;
                {
                    var jsonString = client.DownloadString("https://api.coinmarketcap.com/v1/ticker/?convert=USD");
                    var responseArray = JsonConvert.DeserializeObject<JArray>(jsonString);
                    foreach (JContainer currency in responseArray) {
                        try {
                            if ((string)currency["id"] == "ethereum")
                                USDETH = double.Parse((string)currency["price_usd"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                        try {
                            if ((string)currency["id"] == "monero")
                                USDXMR = double.Parse((string)currency["price_usd"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    }
                }

                if (mCurrentPool == "NiceHash" && radioButtonEthereum.Checked && textBoxBitcoinAddress.Text != "") {
                    double balance = 0;
                    var jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var result = (JContainer)response["result"];
                    var stats = (JArray)result["stats"];
                    foreach (JContainer item in stats)
                        try {
                            balance += double.Parse((string)item["balance"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " BTC (" + string.Format("{0:N2}", balance * USDBTC) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "" && !DevFeeMode) {
                        double price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                        result = (JContainer)response["result"];
                        stats = (JArray)result["stats"];
                        foreach (JContainer item in stats)
                            try {
                                if ((double)item["algo"] == 20)
                                    price = double.Parse((string)item["price"], System.Globalization.CultureInfo.InvariantCulture) * totalSpeed / 1000000000.0;
                            } catch (Exception) { }
                        labelPriceDay.Text = string.Format("{0:N6}", price) + " BTC/Day (" + string.Format("{0:N2}", price * USDBTC) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " BTC/Week (" + string.Format("{0:N2}", price * 7 * USDBTC) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " BTC/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDBTC) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "NiceHash" && radioButtonEthereumPascal.Checked && textBoxBitcoinAddress.Text != "") {
                    double balance = 0;
                    var jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var result = (JContainer)response["result"];
                    var stats = (JArray)result["stats"];
                    foreach (JContainer item in stats)
                        try {
                            balance += double.Parse((string)item["balance"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " BTC (" + string.Format("{0:N2}", balance * USDBTC) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "" && !DevFeeMode) {
                        double ethereum_price = 0;
                        double pascal_price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                        result = (JContainer)response["result"];
                        stats = (JArray)result["stats"];
                        foreach (JContainer item in stats)
                            try {
                                if ((double)item["algo"] == 20)
                                    ethereum_price = double.Parse((string)item["price"], System.Globalization.CultureInfo.InvariantCulture) * totalSpeed / 1000000000.0;
                                if ((double)item["algo"] == 25)
                                    pascal_price = double.Parse((string)item["price"], System.Globalization.CultureInfo.InvariantCulture) * secondaryTotalSpeed / 1000000000000.0;
                            } catch (Exception) { }
                        double price = ethereum_price + pascal_price;
                        labelPriceDay.Text = string.Format("{0:N6}", price) + " BTC/Day (" + string.Format("{0:N2}", price * USDBTC) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " BTC/Week (" + string.Format("{0:N2}", price * 7 * USDBTC) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " BTC/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDBTC) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "NiceHash" && radioButtonMonero.Checked && textBoxBitcoinAddress.Text != "") {
                    double balance = 0;
                    var jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var result = (JContainer)response["result"];
                    var stats = (JArray)result["stats"];
                    foreach (JContainer item in stats)
                        try {
                            balance += double.Parse((string)item["balance"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " BTC (" + string.Format("{0:N2}", balance * USDBTC) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "" && !DevFeeMode) {
                        double price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                        result = (JContainer)response["result"];
                        stats = (JArray)result["stats"];
                        foreach (JContainer item in stats)
                            try {
                                if ((double)item["algo"] == 22)
                                    price = double.Parse((string)item["price"], System.Globalization.CultureInfo.InvariantCulture) * totalSpeed / 1000000.0;
                            } catch (Exception) { }
                        labelPriceDay.Text = string.Format("{0:N6}", price) + " BTC/Day (" + string.Format("{0:N2}", price * USDBTC) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " BTC/Week (" + string.Format("{0:N2}", price * 7 * USDBTC) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " BTC/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDBTC) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "NiceHash" && radioButtonLbry.Checked && textBoxBitcoinAddress.Text != "") {
                    double balance = 0;
                    var jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var result = (JContainer)response["result"];
                    var stats = (JArray)result["stats"];
                    foreach (JContainer item in stats)
                        try {
                            balance += double.Parse((string)item["balance"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " BTC (" + string.Format("{0:N2}", balance * USDBTC) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "" && !DevFeeMode) {
                        double price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                        result = (JContainer)response["result"];
                        stats = (JArray)result["stats"];
                        foreach (JContainer item in stats)
                            try {
                                if ((double)item["algo"] == 23)
                                    price = double.Parse((string)item["price"], System.Globalization.CultureInfo.InvariantCulture) * totalSpeed / 1000000000000.0;
                            } catch (Exception) { }
                        labelPriceDay.Text = string.Format("{0:N6}", price) + " BTC/Day (" + string.Format("{0:N2}", price * USDBTC) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " BTC/Week (" + string.Format("{0:N2}", price * 7 * USDBTC) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " BTC/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDBTC) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "NiceHash" && radioButtonPascal.Checked && textBoxBitcoinAddress.Text != "") {
                    double balance = 0;
                    var jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var result = (JContainer)response["result"];
                    var stats = (JArray)result["stats"];
                    foreach (JContainer item in stats)
                        try {
                            balance += double.Parse((string)item["balance"], System.Globalization.CultureInfo.InvariantCulture);
                        } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " BTC (" + string.Format("{0:N2}", balance * USDBTC) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "" && !DevFeeMode) {
                        double pascal_price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                        result = (JContainer)response["result"];
                        stats = (JArray)result["stats"];
                        foreach (JContainer item in stats)
                            try {
                                if ((double)item["algo"] == 25)
                                    pascal_price = double.Parse((string)item["pascal_price"], System.Globalization.CultureInfo.InvariantCulture) * totalSpeed / 1000000000000.0;
                            } catch (Exception) { }
                        labelPriceDay.Text = string.Format("{0:N6}", pascal_price) + " BTC/Day (" + string.Format("{0:N2}", pascal_price * USDBTC) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", pascal_price * 7) + " BTC/Week (" + string.Format("{0:N2}", pascal_price * 7 * USDBTC) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", pascal_price * (365.25 / 12)) + " BTC/Month (" + string.Format("{0:N2}", pascal_price * (365.25 / 12) * USDBTC) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "ethermine.org" && radioButtonEthereum.Checked && textBoxEthereumAddress.Text != "") {
                    var jsonString = client.DownloadString("https://api.ethermine.org/miner/" + textBoxEthereumAddress.Text + "/currentStats");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var data = (JContainer)response["data"];
                    var balance = (double)data["unpaid"] * 1e-18;
                    var averageHashrate = (double)data["averageHashrate"];
                    var coinsPerMin = (double)data["coinsPerMin"];
                    labelBalance.Text = string.Format("{0:N6}", balance) + " ETH (" + string.Format("{0:N2}", balance * USDETH) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && averageHashrate != 0) {
                        var price = coinsPerMin * 60 * 24 * (totalSpeed / averageHashrate);

                        labelPriceDay.Text = string.Format("{0:N6}", price) + " ETH/Day (" + string.Format("{0:N2}", price * USDETH) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " ETH/Week (" + string.Format("{0:N2}", price * 7 * USDETH) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " ETH/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDETH) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "ethpool.org" && radioButtonEthereum.Checked && textBoxEthereumAddress.Text != "") {
                    var jsonString = client.DownloadString("http://api.ethpool.org/miner/" + textBoxEthereumAddress.Text + "/currentStats");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var data = (JContainer)response["data"];
                    double balance = 0;
                    try {
                        balance = (double)data["unpaid"] * 1e-18;
                    } catch (Exception) { }
                    var averageHashrate = (double)data["averageHashrate"];
                    var coinsPerMin = (double)data["coinsPerMin"];
                    labelBalance.Text = string.Format("{0:N6}", balance) + " ETH (" + string.Format("{0:N2}", balance * USDETH) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && averageHashrate != 0) {
                        var price = coinsPerMin * 60 * 24 * (totalSpeed / averageHashrate);

                        labelPriceDay.Text = string.Format("{0:N6}", price) + " ETH/Day (" + string.Format("{0:N2}", price * USDETH) + " USD/Day)";
                        labelPriceWeek.Text = string.Format("{0:N6}", price * 7) + " ETH/Week (" + string.Format("{0:N2}", price * 7 * USDETH) + " USD/Week)";
                        labelPriceMonth.Text = string.Format("{0:N6}", price * (365.25 / 12)) + " ETH/Month (" + string.Format("{0:N2}", price * (365.25 / 12) * USDETH) + " USD/Month)";
                    } else {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                } else if (mCurrentPool == "Nanopool" && radioButtonEthereum.Checked && textBoxEthereumAddress.Text != "") {
                    var jsonString = client.DownloadString("https://api.nanopool.org/v1/eth/user/" + textBoxEthereumAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var data = (JContainer)response["data"];
                    double balance = 0;
                    try {
                        balance = (double)data["balance"];
                    } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " ETH (" + string.Format("{0:N2}", balance * USDETH) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                } else if (mCurrentPool == "Nanopool" && radioButtonMonero.Checked && textBoxMoneroAddress.Text != "") {
                    var jsonString = client.DownloadString("https://api.nanopool.org/v1/xmr/user/" + textBoxMoneroAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    var data = (JContainer)response["data"];
                    double balance = 0;
                    try {
                        balance = (double)data["balance"];
                    } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " XMR (" + string.Format("{0:N2}", balance * USDXMR) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                } else if (mCurrentPool == "DwarfPool" && radioButtonEthereum.Checked && textBoxEthereumAddress.Text != "") {
                    var jsonString = client.DownloadString("http://dwarfpool.com/eth/api?wallet=" + textBoxEthereumAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    double balance = 0;
                    try {
                        balance = double.Parse((string)response["wallet_balance"], System.Globalization.CultureInfo.InvariantCulture);
                    } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " ETH (" + string.Format("{0:N2}", balance * USDETH) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                } else if (mCurrentPool == "DwarfPool" && radioButtonMonero.Checked && textBoxMoneroAddress.Text != "") {
                    var jsonString = client.DownloadString("http://dwarfpool.com/xmr/api?wallet=" + textBoxMoneroAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                    double balance = 0;
                    try {
                        balance = double.Parse((string)response["wallet_balance"], System.Globalization.CultureInfo.InvariantCulture);
                    } catch (Exception) { }
                    labelBalance.Text = string.Format("{0:N6}", balance) + " XMR (" + string.Format("{0:N2}", balance * USDXMR) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                } else {
                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                    labelBalance.Text = "-";
                }
            } catch (Exception ex) {
                Logger("Exception: " + ex.Message + ex.StackTrace);
            }
        }

        private string ConvertHashRateToString(double totalSpeed) {
            if (totalSpeed < 1000)
                return string.Format("{0:N1} h/s", totalSpeed);
            else if (totalSpeed < 10000)
                return string.Format("{0:N0} h/s", totalSpeed);
            else if (totalSpeed < 100000)
                return string.Format("{0:N2} Kh/s", totalSpeed / 1000);
            else if (totalSpeed < 1000000)
                return string.Format("{0:N1} Kh/s", totalSpeed / 1000);
            else if (totalSpeed < 10000000)
                return string.Format("{0:N0} Kh/s", totalSpeed / 1000);
            else if (totalSpeed < 100000000)
                return string.Format("{0:N2} Mh/s", totalSpeed / 1000000);
            else if (totalSpeed < 1000000000)
                return string.Format("{0:N1} Mh/s", totalSpeed / 1000000);
            else
                return string.Format("{0:N0} Mh/s", totalSpeed / 1000000);
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void UpdateStatsWithShortPolling() {
            try {
                // Pool
                mCurrentPool = (appState == ApplicationGlobalState.Mining && mPrimaryStratum != null) ? (mPrimaryStratum.PoolName + ((mSecondaryStratum != null && mSecondaryStratum.PoolName != mPrimaryStratum.PoolName) ? ", " + mSecondaryStratum.PoolName : "")) :
                               checkBoxCustomPool0Enable.Checked ? (textBoxCustomPool0Host.Text + (comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex != 0 ? (", " + textBoxCustomPool0SecondaryHost.Text) : "")) :
                               checkBoxCustomPool1Enable.Checked ? (textBoxCustomPool1Host.Text + (comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex != 0 ? (", " + textBoxCustomPool1SecondaryHost.Text) : "")) :
                               checkBoxCustomPool2Enable.Checked ? (textBoxCustomPool2Host.Text + (comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex != 0 ? (", " + textBoxCustomPool2SecondaryHost.Text) : "")) :
                               checkBoxCustomPool3Enable.Checked ? (textBoxCustomPool3Host.Text + (comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex != 0 ? (", " + textBoxCustomPool3SecondaryHost.Text) : "")) :
                               (string)listBoxPoolPriorities.Items[0];
                if (appState == ApplicationGlobalState.Mining && mDevFeeMode) {
                    labelCurrentPool.Text = "DEVFEE(" + mDevFeePercentage + "%; " + string.Format("{0:N0}", mDevFeeDurationInSeconds - (DateTime.Now - mDevFeeModeStartTime).TotalSeconds) + " seconds remaining...)";
                } else if (appState == ApplicationGlobalState.Mining && !CustomPoolEnabled && mPrimaryStratum != null && mSecondaryStratum != null) {
                    labelCurrentPool.Text = mCurrentPool + " (" + mPrimaryStratum.ServerAddress + ", " + mSecondaryStratum.ServerAddress + ")";
                } else if (appState == ApplicationGlobalState.Mining && !CustomPoolEnabled && mPrimaryStratum != null) {
                    labelCurrentPool.Text = mCurrentPool + " (" + mPrimaryStratum.ServerAddress + ")";
                } else {
                    labelCurrentPool.Text = mCurrentPool;
                }

                for (var i = 0; i < mDevices.Length; ++i) {
                    var labelColor = checkBoxGPUEnableArray[i].Checked ? Color.Black : Color.Gray;
                    labelGPUNameArray[i].ForeColor = labelColor;
                    labelGPUVendorArray[i].ForeColor = labelColor;
                    labelGPUIDArray[i].ForeColor = labelColor;
                    labelGPUSpeedArray[i].ForeColor = labelColor;
                    labelGPUActivityArray[i].ForeColor = labelColor;
                    labelGPUFanArray[i].ForeColor = labelColor;
                    labelGPUCoreClockArray[i].ForeColor = labelColor;
                    labelGPUMemoryClockArray[i].ForeColor = labelColor;
                    labelGPUSharesArray[i].ForeColor = labelColor;
                }

                var elapsedTimeInSeconds = (long)(DateTime.Now - mStartTime).TotalSeconds;
                if (appState != ApplicationGlobalState.Mining)
                    labelElapsedTime.Text = "-";
                else if (elapsedTimeInSeconds >= 24 * 60 * 60)
                    labelElapsedTime.Text = string.Format("{3} Days {2:00}:{1:00}:{0:00}", elapsedTimeInSeconds % 60, elapsedTimeInSeconds / 60 % 60, elapsedTimeInSeconds / 60 / 60 % 24, elapsedTimeInSeconds / 60 / 60 / 24);
                else
                    labelElapsedTime.Text = string.Format("{2:00}:{1:00}:{0:00}", elapsedTimeInSeconds % 60, elapsedTimeInSeconds / 60 % 60, elapsedTimeInSeconds / 60 / 60 % 24);

                Dictionary<string, double> speeds = new Dictionary<string, double>();
                foreach (var miner in mActiveMiners) {
                    if (!speeds.ContainsKey(miner.FirstAlgorithmName)) {
                        speeds.Add(miner.FirstAlgorithmName, miner.Speed);
                    } else {
                        speeds[miner.FirstAlgorithmName] += miner.Speed;
                    }
                }
                foreach (var miner in mActiveMiners) {
                    if (miner.SecondAlgorithmName == null || miner.SecondAlgorithmName == "")
                        continue;
                    if (!speeds.ContainsKey(miner.SecondAlgorithmName)) {
                        speeds.Add(miner.SecondAlgorithmName, miner.SecondSpeed);
                    } else {
                        speeds[miner.SecondAlgorithmName] += miner.SecondSpeed;
                    }
                }
                labelCurrentSpeed.Text = "-";
                foreach (var algorithm in speeds.Keys) {
                    if (labelCurrentSpeed.Text == "-") {
                        labelCurrentSpeed.Text = ConvertHashRateToString(speeds[algorithm]) + " (" + algorithm + ")";
                    } else {
                        labelCurrentSpeed.Text += ", " + ConvertHashRateToString(speeds[algorithm]) + " (" + algorithm + ")";
                    }
                }

                try { DeviceManagementLibrariesMutex.WaitOne(5000); } catch (Exception) { }
                foreach (var device in mDevices) {
                    var computeDevice = device.GetComputeDevice();
                    var deviceIndex = device.DeviceIndex;
                    double speedPrimary = 0, speedSecondary = 0;
                    foreach (var miner in mActiveMiners)
                        if (miner.DeviceIndex == device.DeviceIndex)
                            speedPrimary += miner.Speed;
                    foreach (var miner in mActiveMiners)
                        if (miner.DeviceIndex == device.DeviceIndex && miner.SecondAlgorithmName != null && miner.SecondAlgorithmName != "")
                            speedSecondary += miner.SecondSpeed;
                    labelGPUSpeedArray[deviceIndex].Text = (appState != ApplicationGlobalState.Mining) ? "-" :
                                                           speedSecondary > 0 ? ConvertHashRateToString(speedPrimary) + ", " + ConvertHashRateToString(speedSecondary) :
                                                                                                         ConvertHashRateToString(speedPrimary);

                    if (device.AcceptedShares + device.RejectedShares == 0) {
                        labelGPUSharesArray[deviceIndex].ForeColor = Color.Black;
                        labelGPUSharesArray[deviceIndex].Text = appState == ApplicationGlobalState.Mining ? "0" : "-";
                    } else {
                        var acceptanceRate = (double)device.AcceptedShares / (device.AcceptedShares + device.RejectedShares);
                        labelGPUSharesArray[deviceIndex].Text = device.AcceptedShares.ToString() + " (" + string.Format("{0:N1}", acceptanceRate * 100) + "%)";
                        labelGPUSharesArray[deviceIndex].ForeColor = acceptanceRate >= 0.99 ? Color.Green : acceptanceRate >= 0.95 ? Color.Goldenrod : Color.Red; // TODO
                    }

                    if (ADLAdapterIndexArray[deviceIndex] >= 0) {
                        // temperature
                        ADLTemperature OSADLTemperatureData;
                        OSADLTemperatureData = new ADLTemperature();
                        var tempBuffer = IntPtr.Zero;
                        var size = Marshal.SizeOf(OSADLTemperatureData);
                        tempBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSADLTemperatureData, tempBuffer, false);

                        if (null != ADL.ADL_Overdrive5_Temperature_Get) {
                            var ADLRet = ADL.ADL_Overdrive5_Temperature_Get(ADLAdapterIndexArray[deviceIndex], 0, tempBuffer);
                            if (ADL.ADL_SUCCESS == ADLRet) {
                                OSADLTemperatureData = (ADLTemperature)Marshal.PtrToStructure(tempBuffer, OSADLTemperatureData.GetType());
                                labelGPUTempArray[deviceIndex].Text = (OSADLTemperatureData.Temperature / 1000).ToString() + "℃";
                                labelGPUTempArray[deviceIndex].ForeColor = OSADLTemperatureData.Temperature >= 80000 ? Color.Red :
                                                                           OSADLTemperatureData.Temperature >= 60000 ? Color.Purple :
                                                                                                                         Color.Blue;
                            }
                        }

                        // activity
                        ADLPMActivity OSADLPMActivityData;
                        OSADLPMActivityData = new ADLPMActivity();
                        var activityBuffer = IntPtr.Zero;
                        size = Marshal.SizeOf(OSADLPMActivityData);
                        activityBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSADLPMActivityData, activityBuffer, false);

                        if (null != ADL.ADL_Overdrive5_CurrentActivity_Get) {
                            var ADLRet = ADL.ADL_Overdrive5_CurrentActivity_Get(ADLAdapterIndexArray[deviceIndex], activityBuffer);
                            if (ADL.ADL_SUCCESS == ADLRet) {
                                OSADLPMActivityData = (ADLPMActivity)Marshal.PtrToStructure(activityBuffer, OSADLPMActivityData.GetType());
                                labelGPUActivityArray[deviceIndex].Text = OSADLPMActivityData.iActivityPercent.ToString() + "%";
                                labelGPUCoreClockArray[deviceIndex].Text = (OSADLPMActivityData.iEngineClock / 100).ToString() + " MHz";
                                labelGPUMemoryClockArray[deviceIndex].Text = (OSADLPMActivityData.iMemoryClock / 100).ToString() + " MHz";
                            }
                        }

                        // fan speed
                        ADLFanSpeedValue OSADLFanSpeedValueData;
                        OSADLFanSpeedValueData = new ADLFanSpeedValue();
                        var fanSpeedValueBuffer = IntPtr.Zero;
                        size = Marshal.SizeOf(OSADLFanSpeedValueData);
                        OSADLFanSpeedValueData.iSpeedType = 1;
                        fanSpeedValueBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSADLFanSpeedValueData, fanSpeedValueBuffer, false);

                        if (null != ADL.ADL_Overdrive5_FanSpeed_Get) {
                            var ADLRet = ADL.ADL_Overdrive5_FanSpeed_Get(ADLAdapterIndexArray[deviceIndex], 0, fanSpeedValueBuffer);
                            if (ADL.ADL_SUCCESS == ADLRet) {
                                OSADLFanSpeedValueData = (ADLFanSpeedValue)Marshal.PtrToStructure(fanSpeedValueBuffer, OSADLFanSpeedValueData.GetType());
                                labelGPUFanArray[deviceIndex].Text = OSADLFanSpeedValueData.iFanSpeed.ToString() + "%";
                            }
                        }
                    } else if (NVMLInitialized && device.GetComputeDevice().Vendor.Equals("NVIDIA Corporation")) {
                        uint temp = 0;
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetTemperature(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlTemperatureSensors.Gpu, ref temp);
                        labelGPUTempArray[deviceIndex].Text = temp.ToString() + "℃";
                        labelGPUTempArray[deviceIndex].ForeColor = temp >= 80 ? Color.Red :
                                                                   temp >= 60 ? Color.Purple :
                                                                                  Color.Blue;

                        uint fanSpeed = 0;
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetFanSpeed(nvmlDeviceArray[deviceIndex], ref fanSpeed);
                        labelGPUFanArray[deviceIndex].Text = fanSpeed.ToString() + "%";

                        var utilization = new ManagedCuda.Nvml.nvmlUtilization();
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetUtilizationRates(nvmlDeviceArray[deviceIndex], ref utilization);
                        labelGPUActivityArray[deviceIndex].Text = utilization.gpu.ToString() + "%";

                        uint clock = 0;
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetClockInfo(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlClockType.Graphics, ref clock);
                        labelGPUCoreClockArray[deviceIndex].Text = clock.ToString() + " MHz";
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetClockInfo(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlClockType.Mem, ref clock);
                        labelGPUMemoryClockArray[deviceIndex].Text = clock.ToString() + " MHz";
                    }
                }
            } catch (Exception ex) {
                Logger("Exception: " + ex.Message + ex.StackTrace);
            } finally {
                try { try { DeviceManagementLibrariesMutex.ReleaseMutex(); } catch (Exception) { } } catch (Exception) { }
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            UpdateDatabase();
            UnloadPhyMemDriver();
            if (ADLInitialized && null != ADL.ADL_Main_Control_Destroy)
                ADL.ADL_Main_Control_Destroy();
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void timerDeviceStatusUpdates_Tick(object sender, EventArgs e) {
            try {
                UpdateStatsWithShortPolling();
            } catch (Exception ex) {
                Logger("Exception: " + ex.Message + ex.StackTrace);
            }
        }

        public bool ValidateBitcoinAddress() {
            var regex = new System.Text.RegularExpressions.Regex("^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$");
            var match = regex.Match(textBoxBitcoinAddress.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid Bitcoin address.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidateEthereumAddress() {
            var regex = new System.Text.RegularExpressions.Regex("^0x[a-fA-Z0-9]{40}$");
            var match = regex.Match(textBoxEthereumAddress.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid Ethereum address starting with \"0x\".", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidateMoneroAddress() {
            var regex = new System.Text.RegularExpressions.Regex(@"^(4[0-9AB][123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz]{93}(\.?(([0-9a-fA-F]{16})|([0-9a-fA-F]{64})))?)|([0-9a-fA-F]{64})?$");
            var match = regex.Match(textBoxMoneroAddress.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid Monero address.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidatePascalAddress() {
            var regex = new System.Text.RegularExpressions.Regex(@"^[\-0-9a-zA-Z\.]+$");
            var match = regex.Match(textBoxPascalAddress.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid Pascal address.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidateLbryAddress() {
            var regex = new System.Text.RegularExpressions.Regex(@"^[0-9a-zA-Z]+?$");
            var match = regex.Match(textBoxLbryAddress.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid Lbry address.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidateRigID() {
            var regex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9]+$");
            var match = regex.Match(textBoxRigID.Text);
            if (match.Success) {
                return true;
            } else {
                MessageBox.Show("Please enter a valid rig ID consisting of alphanumeric characters.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private struct StratumServerInfo : IComparable<StratumServerInfo>
        {
            public string name;
            public long delay;
            public long time;

            public StratumServerInfo(string aName, long aDelay) {
                name = aName;
                delay = aDelay;
                try {
                    time = Utilities.MeasurePingRoundtripTime(aName);
                } catch (Exception) {
                    time = -1;
                }
                if (time >= 0)
                    time += delay;
            }

            public int CompareTo(StratumServerInfo other) {
                if (time == other.time)
                    return 0;
                else if (other.time < 0 && time >= 0)
                    return -1;
                else if (other.time >= 0 && time < 0)
                    return 1;
                else if (other.time > time)
                    return -1;
                else
                    return 1;
            }
        };

        #region GetServers

        List<StratumServerInfo> GetNiceHashLbryServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("lbry.usa.nicehash.com", 0),
                new StratumServerInfo("lbry.eu.nicehash.com", 0),
                new StratumServerInfo("lbry.hk.nicehash.com", 150),
                new StratumServerInfo("lbry.jp.nicehash.com", 100),
                new StratumServerInfo("lbry.in.nicehash.com", 200),
                new StratumServerInfo("lbry.br.nicehash.com", 180)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNiceHashEthashServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("daggerhashimoto.usa.nicehash.com", 0),
                new StratumServerInfo("daggerhashimoto.eu.nicehash.com", 0),
                new StratumServerInfo("daggerhashimoto.hk.nicehash.com", 150),
                new StratumServerInfo("daggerhashimoto.jp.nicehash.com", 100),
                new StratumServerInfo("daggerhashimoto.in.nicehash.com", 200),
                new StratumServerInfo("daggerhashimoto.br.nicehash.com", 180)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNanopoolEthashServers() {
            var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("eth-eu1.nanopool.org", 0),
                                new StratumServerInfo("eth-eu2.nanopool.org", 0),
                                new StratumServerInfo("eth-asia1.nanopool.org", 0),
                                new StratumServerInfo("eth-us-east1.nanopool.org", 0),
                                new StratumServerInfo("eth-us-west1.nanopool.org", 0)
                            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNiceHashCryptoNightServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("cryptonight.usa.nicehash.com", 0),
                new StratumServerInfo("cryptonight.eu.nicehash.com", 0),
                new StratumServerInfo("cryptonight.hk.nicehash.com", 150),
                new StratumServerInfo("cryptonight.jp.nicehash.com", 100),
                new StratumServerInfo("cryptonight.in.nicehash.com", 200),
                new StratumServerInfo("cryptonight.br.nicehash.com", 180)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNiceHashPascalServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("pascal.usa.nicehash.com", 0),
                new StratumServerInfo("pascal.eu.nicehash.com", 0),
                new StratumServerInfo("pascal.hk.nicehash.com", 150),
                new StratumServerInfo("pascal.jp.nicehash.com", 100),
                new StratumServerInfo("pascal.in.nicehash.com", 200),
                new StratumServerInfo("pascal.br.nicehash.com", 180)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetDwarfPoolCryptoNightServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("xmr-eu.dwarfpool.com", 0),
                new StratumServerInfo("xmr-usa.dwarfpool.com", 0)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNanopoolCryptoNightServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("xmr-eu1.nanopool.org", 0),
                new StratumServerInfo("xmr-eu2.nanopool.org", 0),
                new StratumServerInfo("xmr-us-east1.nanopool.org", 0),
                new StratumServerInfo("xmr-us-west1.nanopool.org", 0),
                new StratumServerInfo("xmr-asia1.nanopool.org", 0)
            };
            hosts.Sort();
            return hosts;
        }

        List<StratumServerInfo> GetNanopoolPascalServers() {
            var hosts = new List<StratumServerInfo> {
                new StratumServerInfo("pasc-eu1.nanopool.org", 0),
                new StratumServerInfo("pasc-eu2.nanopool.org", 0),
                new StratumServerInfo("pasc-us-east1.nanopool.org", 0),
                new StratumServerInfo("pasc-us-west1.nanopool.org", 0),
                new StratumServerInfo("pasc-asia1.nanopool.org", 0),
                new StratumServerInfo("pasc-jp1.nanopool.org", 0),
                new StratumServerInfo("pasc-au1.nanopool.org", 0)
            };
            hosts.Sort();
            return hosts;
        }

        #endregion

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public void LaunchOpenCLCryptoNightMiners(string pool) {
            CryptoNightStratum stratum = null;
            var niceHashMode = false;

            if (pool == "NiceHash") {
                var hosts = GetNiceHashCryptoNightServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeBitcoinAddress + ".DEVFEE" : textBoxBitcoinAddress.Text;
                            if (!mDevFeeMode && textBoxRigID.Text != "")
                                username += "." + textBoxRigID.Text; // TODO
                            stratum = new CryptoNightStratum(host.name, 3355, username, "x", pool);
                            break;
                        } catch (Exception ex) { Logger("Exception: " + ex.Message + ex.StackTrace); }
                niceHashMode = true;
            } else if (pool == "DwarfPool") {
                var hosts = GetDwarfPoolCryptoNightServers();
                hosts.Sort();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeMoneroAddress : textBoxMoneroAddress.Text;
                            //if (!mDevFeeMode && textBoxRigID.Text != "")
                            //    username += "." + textBoxRigID.Text; // TODO
                            stratum = new CryptoNightStratum(host.name, 8005, username, textBoxEmail.Text != "" ? textBoxEmail.Text : "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "Nanopool") {
                var hosts = GetNanopoolCryptoNightServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeMoneroAddress : textBoxMoneroAddress.Text;
                            //if (!mDevFeeMode && textBoxRigID.Text != "") {
                            //    username += "." + textBoxRigID.Text; // TODO
                            //    if (textBoxEmail.Text != "")
                            //        username += "/" + textBoxEmail.Text;
                            //}
                            stratum = new CryptoNightStratum(host.name, 14444, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "mineXMR.com") {
                var username = mDevFeeMode ? mDevFeeMoneroAddress : textBoxMoneroAddress.Text;
                //if (!mDevFeeMode && textBoxRigID.Text != "")
                //    username += "." + textBoxRigID.Text; // TODO
                stratum = new CryptoNightStratum("pool.minexmr.com", 7777, username, "x", pool);
            }

            if (stratum != null) {
                LaunchOpenCLCryptoNightMinersWithStratum(stratum, niceHashMode);
                mPrimaryStratum = (Stratum)stratum;
            }
        }

        void LaunchOpenCLCryptoNightMinersWithStratum(CryptoNightStratum stratum, bool niceHashMode) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, i, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    for (i = 0; i < numericUpDownDeviceCryptoNightThreadsArray[deviceIndex].Value; ++i)
                        ++minerCount;
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    for (i = 0; i < numericUpDownDeviceCryptoNightThreadsArray[deviceIndex].Value; ++i) {
                        OpenCLCryptoNightMiner miner = null;
                        foreach (var inactiveMiner in mInactiveMiners) {
                            if (inactiveMiner.GetType() == typeof(OpenCLCryptoNightMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                                miner = (OpenCLCryptoNightMiner)inactiveMiner;
                                break;
                            }
                        }
                        if (miner != null) {
                            mInactiveMiners.Remove((Miner)miner);
                        } else {
                            miner = new OpenCLCryptoNightMiner(mDevices[deviceIndex]);
                        }
                        mActiveMiners.Add(miner);
                        miner.Start(stratum,
                            Convert.ToInt32(Math.Round(numericUpDownDeviceCryptoNightRawIntensityArray[deviceIndex]
                                .Value)),
                            Convert.ToInt32(Math.Round(numericUpDownDeviceCryptoNightLocalWorkSizeArray[deviceIndex]
                                .Value)), niceHashMode);
                        toolStripMainFormProgressBar.Value = ++minerCount;
                        for (int j = 0; j < mLaunchInterval; j += 10) {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        } 
                    }
                }
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public void LaunchOpenCLLbryMiners(string pool) {
            LbryStratum stratum = null;
            string username = "";

            if (pool == "NiceHash" 
                && (username = mDevFeeMode ? mDevFeeBitcoinAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxBitcoinAddress.Text + "." + textBoxRigID.Text) : textBoxBitcoinAddress.Text)).Length > 0) {
                var hosts = GetNiceHashLbryServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            stratum = new LbryStratum(host.name, 3356, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "zpool" && (username = mDevFeeMode ? mDevFeeBitcoinAddress : textBoxBitcoinAddress.Text).Length > 0) {
                stratum = new LbryStratum("lbry.mine.zpool.ca", 3334, username, "c=LBC", pool);
            }

            if (stratum != null) {
                LaunchOpenCLLbryMinersWithStratum(stratum);
                mPrimaryStratum = (Stratum)stratum;
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public void LaunchOpenCLDualEthashPascalMiners(string pool) {
            EthashStratum ethashStratum = null;
            PascalStratum pascalStratum = null;

            if (pool == "NiceHash") {
                var username = mDevFeeMode ? mDevFeeBitcoinAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxBitcoinAddress.Text + "." + textBoxRigID.Text) : textBoxBitcoinAddress.Text);
                var ethashHosts = GetNiceHashEthashServers();
                foreach (var ethashHost in ethashHosts)
                    if (ethashHost.time >= 0)
                        try {
                            ethashStratum = new NiceHashEthashStratum(ethashHost.name, 3353, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                var pascalHosts = GetNiceHashPascalServers();
                foreach (var pascalHost in pascalHosts)
                    if (pascalHost.time >= 0)
                        try {
                            pascalStratum = new PascalStratum(pascalHost.name, 3358, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "Nanopool") {
                var username = mDevFeeMode ? mDevFeeEthereumAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxEthereumAddress.Text + "." + textBoxRigID.Text) : textBoxEthereumAddress.Text);
                var ethashHosts = GetNanopoolEthashServers();
                foreach (var ethashHost in ethashHosts)
                    if (ethashHost.time >= 0)
                        try {
                            ethashStratum = new OpenEthereumPoolEthashStratum(ethashHost.name, 9999, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                username = mDevFeeMode ? mDevFeePascalAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxPascalAddress.Text + "." + textBoxRigID.Text) : textBoxPascalAddress.Text);
                var pascalHosts = GetNanopoolPascalServers();
                foreach (var pascalHost in pascalHosts)
                    if (pascalHost.time >= 0)
                        try {
                            pascalStratum = new PascalStratum(pascalHost.name, 15555, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            }

            if (ethashStratum != null && pascalStratum != null) {
                LaunchOpenCLDualEthashPascalMinersWithStratum(ethashStratum, pascalStratum);
                mPrimaryStratum = (Stratum)ethashStratum;
                mSecondaryStratum = (Stratum)pascalStratum;
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public void LaunchPascalMiners(string pool) {
            PascalStratum stratum = null;

            if (pool == "NiceHash") {
                var username = mDevFeeMode ? mDevFeeBitcoinAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxBitcoinAddress.Text + "." + textBoxRigID.Text) : textBoxBitcoinAddress.Text);
                var hosts = GetNiceHashPascalServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            stratum = new PascalStratum(host.name, 3358, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "Nanopool") {
                var username = mDevFeeMode ? mDevFeePascalAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxPascalAddress.Text + "." + textBoxRigID.Text) : textBoxPascalAddress.Text);
                var hosts = GetNanopoolPascalServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            stratum = new PascalStratum(host.name, 15555, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            }

            if (stratum != null) {
                LaunchOpenCLPascalMinersWithStratum(stratum);
                mPrimaryStratum = (Stratum)stratum;
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        public void LaunchEthashMiners(string pool) {
            EthashStratum stratum = null;

            if (pool == "NiceHash") {
                var username = mDevFeeMode ? mDevFeeBitcoinAddress + ".DEVFEE" : (textBoxRigID.Text != "" ? (textBoxBitcoinAddress.Text + textBoxRigID.Text) : textBoxBitcoinAddress.Text);
                var hosts = GetNiceHashEthashServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            stratum = new NiceHashEthashStratum(host.name, 3353, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "DwarfPool") {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("eth-eu.dwarfpool.com", 0),
                                new StratumServerInfo("eth-us.dwarfpool.com", 0),
                                new StratumServerInfo("eth-us2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ru.dwarfpool.com", 0),
                                new StratumServerInfo("eth-asia.dwarfpool.com", 0),
                                new StratumServerInfo("eth-cn.dwarfpool.com", 0),
                                new StratumServerInfo("eth-cn2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-sg.dwarfpool.com", 0),
                                new StratumServerInfo("eth-au.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ru2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-hk.dwarfpool.com", 0),
                                new StratumServerInfo("eth-br.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ar.dwarfpool.com", 0)
                            };
                hosts.Sort();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeEthereumAddress + ".DEVFEE" : textBoxEthereumAddress.Text;
                            if (!mDevFeeMode && textBoxRigID.Text != "")
                                username += "." + textBoxRigID.Text; // TODO
                            stratum = new OpenEthereumPoolEthashStratum(host.name, 8008, username, textBoxEmail.Text != "" ? textBoxEmail.Text : "x", pool, true);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "ethermine.org") {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("us1.ethermine.org", 0),
                                new StratumServerInfo("us2.ethermine.org", 0),
                                new StratumServerInfo("eu1.ethermine.org", 0),
                                new StratumServerInfo("asia1.ethermine.org", 0)
                            };
                hosts.Sort();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeEthereumAddress + ".DEVFEE" : textBoxEthereumAddress.Text;
                            if (!mDevFeeMode && textBoxRigID.Text != "")
                                username += "." + textBoxRigID.Text; // TODO
                            stratum = new OpenEthereumPoolEthashStratum(host.name, 4444, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "ethpool.org") {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("us1.ethpool.org", 0),
                                new StratumServerInfo("us2.ethpool.org", 0),
                                new StratumServerInfo("eu1.ethpool.org", 0),
                                new StratumServerInfo("asia1.ethpool.org", 0)
                            };
                hosts.Sort();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeEthereumAddress + ".DEVFEE" : textBoxEthereumAddress.Text;
                            if (!mDevFeeMode && textBoxRigID.Text != "")
                                username += "." + textBoxRigID.Text; // TODO
                            stratum = new OpenEthereumPoolEthashStratum(host.name, 3333, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            } else if (pool == "Nanopool") {
                var hosts = GetNanopoolEthashServers();
                foreach (var host in hosts)
                    if (host.time >= 0)
                        try {
                            var username = mDevFeeMode ? mDevFeeEthereumAddress + ".DEVFEE" : textBoxEthereumAddress.Text;
                            if (!mDevFeeMode && textBoxRigID.Text != "") {
                                username += "." + textBoxRigID.Text; // TODO
                                if (textBoxEmail.Text != "")
                                    username += "/" + textBoxEmail.Text;
                            }
                            stratum = new OpenEthereumPoolEthashStratum(host.name, 9999, username, "x", pool);
                            break;
                        } catch (Exception ex) {
                            Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
            }

            if (stratum != null) {
                LaunchOpenCLEthashMinersWithStratum(stratum);
                mPrimaryStratum = (Stratum)stratum;
            }
        }

        void LaunchOpenCLDualEthashLbryMinersWithStratum(EthashStratum stratum, LbryStratum stratum2) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    minerCount += 1;
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;

            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    OpenCLDualEthashLbryMiner dualMiner = null;
                    foreach (var inactiveMiner in mInactiveMiners) {
                        if (inactiveMiner.GetType() == typeof(OpenCLDualEthashLbryMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                            dualMiner = (OpenCLDualEthashLbryMiner)inactiveMiner;
                            break;
                        }
                    }
                    if (dualMiner != null) {
                        mInactiveMiners.Remove((Miner)dualMiner);
                    } else {
                        dualMiner = new OpenCLDualEthashLbryMiner(mDevices[deviceIndex]);
                    }
                    mActiveMiners.Add(dualMiner);
                    dualMiner.Start(stratum,
                        Convert.ToInt32(Math.Round(numericUpDownDeviceEthashIntensityArray[deviceIndex]
                            .Value)),
                        Convert.ToInt32(Math.Round(numericUpDownDeviceEthashLocalWorkSizeArray[deviceIndex]
                            .Value)),
                            stratum2,
                            2048 * (int)mDevices[deviceIndex].MaxComputeUnits, //Convert.ToInt32(Math.Round(numericUpDownDeviceLbryIntensityArray[deviceIndex].Value)),
                            64);
                    toolStripMainFormProgressBar.Value = ++minerCount;

                    for (int j = 0; j < mLaunchInterval; j += 10) {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(10);
                    }

                }
            }
        }

        void LaunchOpenCLDualEthashPascalMinersWithStratum(EthashStratum stratum, PascalStratum stratum2) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    minerCount += Convert.ToInt32(Math.Round(numericUpDownDeviceEthashPascalThreadsArray[deviceIndex].Value));
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;

            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    for (int i = 0; i < numericUpDownDeviceEthashPascalThreadsArray[deviceIndex].Value; ++i) {
                        OpenCLDualEthashPascalMiner dualMiner = null;
                        foreach (var inactiveMiner in mInactiveMiners) {
                            if (inactiveMiner.GetType() == typeof(OpenCLDualEthashPascalMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                                dualMiner = (OpenCLDualEthashPascalMiner)inactiveMiner;
                                break;
                            }
                        }
                        if (dualMiner != null) {
                            mInactiveMiners.Remove((Miner)dualMiner);
                        } else {
                            dualMiner = new OpenCLDualEthashPascalMiner(mDevices[deviceIndex]);
                        }
                        mActiveMiners.Add(dualMiner);
                        dualMiner.Start(stratum,
                                stratum2,
                            Convert.ToInt32(Math.Round(numericUpDownDeviceEthashPascalIntensityArray[deviceIndex]
                                .Value)),
                            Convert.ToInt32(Math.Round(numericUpDownDeviceEthashPascalPascalIterationsArray[deviceIndex]
                                .Value)));
                        toolStripMainFormProgressBar.Value = ++minerCount;

                        for (int j = 0; j < mLaunchInterval; j += 10) {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
        }

        void LaunchOpenCLEthashMinersWithStratum(EthashStratum stratum) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, i, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    for (i = 0; i < numericUpDownDeviceEthashThreadsArray[deviceIndex].Value; ++i)
                        ++minerCount;
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    for (i = 0; i < numericUpDownDeviceEthashThreadsArray[deviceIndex].Value; ++i) {
                        OpenCLEthashMiner miner = null;
                        foreach (var inactiveMiner in mInactiveMiners) {
                            if (inactiveMiner.GetType() == typeof(OpenCLEthashMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                                miner = (OpenCLEthashMiner)inactiveMiner;
                                break;
                            }
                        }
                        if (miner != null) {
                            mInactiveMiners.Remove((Miner)miner);
                        } else {
                            miner = new OpenCLEthashMiner(mDevices[deviceIndex]);
                        }
                        mActiveMiners.Add(miner);
                        miner.Start(stratum,
                            Convert.ToInt32(Math.Round(numericUpDownDeviceEthashIntensityArray[deviceIndex]
                                .Value)),
                            Convert.ToInt32(Math.Round(numericUpDownDeviceEthashLocalWorkSizeArray[deviceIndex]
                                .Value)));
                        toolStripMainFormProgressBar.Value = ++minerCount;
                        for (int j = 0; j < mLaunchInterval; j += 10) {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
        }

        void LaunchOpenCLLbryMinersWithStratum(LbryStratum stratum) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, i, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    for (i = 0; i < Convert.ToInt32(numericUpDownDeviceLbryThreadsArray[deviceIndex].Value); ++i)
                        ++minerCount;
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    for (i = 0; i < Convert.ToInt32(numericUpDownDeviceLbryThreadsArray[deviceIndex].Value); ++i) {
                        OpenCLLbryMiner miner = null;
                        foreach (var inactiveMiner in mInactiveMiners) {
                            if (inactiveMiner.GetType() == typeof(OpenCLLbryMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                                miner = (OpenCLLbryMiner)inactiveMiner;
                                break;
                            }
                        }
                        if (miner != null) {
                            mInactiveMiners.Remove((Miner)miner);
                        } else {
                            miner = new OpenCLLbryMiner(mDevices[deviceIndex]);
                        }
                        mActiveMiners.Add(miner);
                        miner.Start(stratum,
                            Convert.ToInt32(Math.Round(numericUpDownDeviceLbryIntensityArray[deviceIndex].Value)),
                            Convert.ToInt32(Math.Round(numericUpDownDeviceLbryLocalWorkSizeArray[deviceIndex].Value)));
                        toolStripMainFormProgressBar.Value = ++minerCount;
                        for (int j = 0; j < mLaunchInterval; j += 10) {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
        }

        void LaunchOpenCLPascalMinersWithStratum(PascalStratum stratum) {
            this.Activate();
            toolStripMainFormProgressBar.Value = toolStripMainFormProgressBar.Minimum = 0;
            int deviceIndex, i, minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex)
                if (checkBoxGPUEnableArray[deviceIndex].Checked)
                    for (i = 0; i < Convert.ToInt32(Math.Round(numericUpDownDevicePascalThreadsArray[deviceIndex].Value)); ++i)
                        ++minerCount;
            toolStripMainFormProgressBar.Maximum = minerCount;
            minerCount = 0;
            for (deviceIndex = 0; deviceIndex < mDevices.Length; ++deviceIndex) {
                if (checkBoxGPUEnableArray[deviceIndex].Checked) {
                    for (i = 0; i < Convert.ToInt32(Math.Round(numericUpDownDevicePascalThreadsArray[deviceIndex].Value)); ++i) {
                        OpenCLPascalMiner miner = null;
                        foreach (var inactiveMiner in mInactiveMiners) {
                            if (inactiveMiner.GetType() == typeof(OpenCLPascalMiner) && deviceIndex == inactiveMiner.DeviceIndex) {
                                miner = (OpenCLPascalMiner)inactiveMiner;
                                break;
                            }
                        }
                        if (miner != null) {
                            mInactiveMiners.Remove((Miner)miner);
                        } else {
                            miner = new OpenCLPascalMiner(mDevices[deviceIndex]);
                        }
                        mActiveMiners.Add(miner);
                        miner.Start(stratum,
                            Convert.ToInt32(Math.Round(numericUpDownDevicePascalIntensityArray[deviceIndex].Value)),
                            Convert.ToInt32(Math.Round(numericUpDownDevicePascalLocalWorkSizeArray[deviceIndex].Value)));
                        toolStripMainFormProgressBar.Value = ++minerCount;
                        for (int j = 0; j < mLaunchInterval; j += 10) {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
        }

        bool CustomPoolEnabled {
            get {
                return checkBoxCustomPool0Enable.Checked || checkBoxCustomPool1Enable.Checked || checkBoxCustomPool2Enable.Checked || checkBoxCustomPool3Enable.Checked;
            }
        }

        private void LaunchMinersForCustomPool(string algo, string host, int port, string login, string password, string algo2, string host2, int port2, string login2, string password2) {
            if (algo == "Ethash" && algo2 == "Lbry") {
                var stratum = new OpenEthereumPoolEthashStratum(host, port, login, password, host);
                var stratum2 = new LbryStratum(host2, port2, login2, password2, host2);
                LaunchOpenCLDualEthashLbryMinersWithStratum(stratum, stratum2);
                mPrimaryStratum = stratum;
                mSecondaryStratum = stratum2;
            } else if (algo == "Ethash" && algo2 == "Pascal") {
                var stratum = new OpenEthereumPoolEthashStratum(host, port, login, password, host);
                var stratum2 = new PascalStratum(host2, port2, login2, password2, host2);
                LaunchOpenCLDualEthashPascalMinersWithStratum(stratum, stratum2);
                mPrimaryStratum = stratum;
                mSecondaryStratum = stratum2;
            } else if (algo == "Ethash") {
                var stratum = new OpenEthereumPoolEthashStratum(host, port, login, password, host);
                LaunchOpenCLEthashMinersWithStratum(stratum);
                mPrimaryStratum = stratum;
            } else if (algo == "CryptoNight") {
                var stratum = new CryptoNightStratum(host, port, login, password, host);
                LaunchOpenCLCryptoNightMinersWithStratum(stratum, false);
                mPrimaryStratum = stratum;
            } else if (algo == "Lbry") {
                var stratum = new LbryStratum(host, port, login, password, host);
                LaunchOpenCLLbryMinersWithStratum(stratum);
                mPrimaryStratum = stratum;
            } else if (algo == "Pascal") {
                var stratum = new PascalStratum(host, port, login, password, host);
                LaunchOpenCLPascalMinersWithStratum(stratum);
                mPrimaryStratum = stratum;
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void LaunchMiners() {
            GC.Collect();

            if (CustomPoolEnabled && !mDevFeeMode) {
                for (int customPoolIndex = 0; customPoolIndex < 4; customPoolIndex++) {
                    bool enabled = (customPoolIndex == 0) ? checkBoxCustomPool0Enable.Checked :
                                   (customPoolIndex == 1) ? checkBoxCustomPool1Enable.Checked :
                                   (customPoolIndex == 2) ? checkBoxCustomPool2Enable.Checked :
                                                            checkBoxCustomPool3Enable.Checked;
                    String host = (customPoolIndex == 0) ? textBoxCustomPool0Host.Text :
                                  (customPoolIndex == 1) ? textBoxCustomPool1Host.Text :
                                  (customPoolIndex == 2) ? textBoxCustomPool2Host.Text :
                                                           textBoxCustomPool3Host.Text;
                    int port = (customPoolIndex == 0) ? Convert.ToInt32(numericUpDownCustomPool0Port.Value) :
                               (customPoolIndex == 1) ? Convert.ToInt32(numericUpDownCustomPool1Port.Value) :
                               (customPoolIndex == 2) ? Convert.ToInt32(numericUpDownCustomPool2Port.Value) :
                                                        Convert.ToInt32(numericUpDownCustomPool3Port.Value);
                    String login = (customPoolIndex == 0) ? textBoxCustomPool0Login.Text :
                                   (customPoolIndex == 1) ? textBoxCustomPool1Login.Text :
                                   (customPoolIndex == 2) ? textBoxCustomPool2Login.Text :
                                                            textBoxCustomPool3Login.Text;
                    String password = (customPoolIndex == 0) ? textBoxCustomPool0Password.Text :
                                      (customPoolIndex == 1) ? textBoxCustomPool1Password.Text :
                                      (customPoolIndex == 2) ? textBoxCustomPool2Password.Text :
                                                               textBoxCustomPool3Password.Text;
                    String host2 = (customPoolIndex == 0) ? textBoxCustomPool0SecondaryHost.Text :
                                   (customPoolIndex == 1) ? textBoxCustomPool1SecondaryHost.Text :
                                   (customPoolIndex == 2) ? textBoxCustomPool2SecondaryHost.Text :
                                                            textBoxCustomPool3SecondaryHost.Text;
                    int port2 = (customPoolIndex == 0) ? Convert.ToInt32(numericUpDownCustomPool0SecondaryPort.Value) :
                                (customPoolIndex == 1) ? Convert.ToInt32(numericUpDownCustomPool1SecondaryPort.Value) :
                                (customPoolIndex == 2) ? Convert.ToInt32(numericUpDownCustomPool2SecondaryPort.Value) :
                                                         Convert.ToInt32(numericUpDownCustomPool3SecondaryPort.Value);
                    String login2 = (customPoolIndex == 0) ? textBoxCustomPool0SecondaryLogin.Text :
                                    (customPoolIndex == 1) ? textBoxCustomPool1SecondaryLogin.Text :
                                    (customPoolIndex == 2) ? textBoxCustomPool2SecondaryLogin.Text :
                                                             textBoxCustomPool3SecondaryLogin.Text;
                    String password2 = (customPoolIndex == 0) ? textBoxCustomPool0SecondaryPassword.Text :
                                       (customPoolIndex == 1) ? textBoxCustomPool1SecondaryPassword.Text :
                                       (customPoolIndex == 2) ? textBoxCustomPool2SecondaryPassword.Text :
                                                                textBoxCustomPool3SecondaryPassword.Text;
                    String algo = (customPoolIndex == 0) ? (string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool0Algorithm.SelectedIndex] :
                                  (customPoolIndex == 1) ? (string)comboBoxCustomPool1Algorithm.Items[comboBoxCustomPool1Algorithm.SelectedIndex] :
                                  (customPoolIndex == 2) ? (string)comboBoxCustomPool2Algorithm.Items[comboBoxCustomPool2Algorithm.SelectedIndex] :
                                                           (string)comboBoxCustomPool3Algorithm.Items[comboBoxCustomPool3Algorithm.SelectedIndex];
                    String algo2 = (customPoolIndex == 0) ? (string)comboBoxCustomPool0SecondaryAlgorithm.Items[comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex] :
                                   (customPoolIndex == 1) ? (string)comboBoxCustomPool1SecondaryAlgorithm.Items[comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex] :
                                   (customPoolIndex == 2) ? (string)comboBoxCustomPool2SecondaryAlgorithm.Items[comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex] :
                                                            (string)comboBoxCustomPool3SecondaryAlgorithm.Items[comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex];

                    if (!enabled)
                        continue;

                    try {
                        Logger("Launching miner(s) for Custom Pool " + customPoolIndex + "...");
                        LaunchMinersForCustomPool(algo, host, port, login, password, algo2, host2, port2, login2, password2);
                        break;
                    } catch (Exception ex) {
                        Logger("Failed to launch miner(s) for Custom Pool " + customPoolIndex + ": " + ex.Message + ex.StackTrace);
                    }

                    // Clean up the mess.
                    if (mPrimaryStratum != null)
                        mPrimaryStratum.Stop();
                    if (mSecondaryStratum != null)
                        mSecondaryStratum.Stop();
                    foreach (Miner miner in mActiveMiners)
                        miner.Stop();
                    mPrimaryStratum = null;
                    mSecondaryStratum = null;
                    mActiveMiners.Clear();
                }
            } else {
                foreach (string pool in listBoxPoolPriorities.Items) {
                    try {
                        if (radioButtonEthereumPascal.Checked) {
                            Logger("Launching Dual Ethash/Pascal for " + pool + "...");
                            LaunchOpenCLDualEthashPascalMiners(pool);
                        } else if (radioButtonEthereum.Checked) {
                            Logger("Launching Ethash miners for " + pool + "...");
                            LaunchEthashMiners(pool);
                        } else if (radioButtonMonero.Checked) {
                            Logger("Launching CryptoNight miners for " + pool + "...");
                            LaunchOpenCLCryptoNightMiners(pool);
                        } else if (radioButtonLbry.Checked) {
                            Logger("Launching Lbry miners for " + pool + "...");
                            LaunchOpenCLLbryMiners(pool);
                        } else if (radioButtonPascal.Checked) {
                            Logger("Launching Pascal miners for " + pool + "...");
                            LaunchPascalMiners(pool);
                        }
                        if (mPrimaryStratum != null && mActiveMiners.Count > 0) {
                            break;
                        } else {
                            Logger("Failed to launch miner(s) for " + pool);
                        }
                    } catch (Exception ex) {
                        Logger("Failed to launch miners for " + pool + ": " + ex.Message + ex.StackTrace);
                    }
                    if (mPrimaryStratum != null && mActiveMiners.Count > 0)
                        break;

                    // Clean up the mess.
                    if (mPrimaryStratum != null)
                        mPrimaryStratum.Stop();
                    if (mSecondaryStratum != null)
                        mSecondaryStratum.Stop();
                    foreach (Miner miner in mActiveMiners)
                        miner.Stop();
                    mPrimaryStratum = null;
                    mSecondaryStratum = null;
                    mActiveMiners.Clear();
                }
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void StopMiners() {
            try {
                Logger("Stopping miners...");
                foreach (var miner in mActiveMiners)
                    miner.Stop();
                var allDone = false;
                var counter = 500;
                while (!allDone && counter-- > 0) {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                    allDone = true;
                    foreach (var miner in mActiveMiners)
                        if (!miner.Done) {
                            allDone = false;
                            break;
                        }
                }
                foreach (var miner in mActiveMiners)
                    if (!miner.Done)
                        miner.Abort(); // Not good at all. Avoid this at all costs.
                mPrimaryStratum.Stop();
                if (mSecondaryStratum != null)
                    mSecondaryStratum.Stop();
                toolStripMainFormProgressBar.Value = 0;
            } catch (Exception ex) {
                Logger("Exception: " + ex.Message + ex.StackTrace);
            }
            foreach (var miner in mActiveMiners)
                mInactiveMiners.Add(miner);
            mActiveMiners.Clear();
            mPrimaryStratum = null;
            mSecondaryStratum = null;

            GC.Collect();
            Logger("Stopped miners.");
        }

        private void buttonStart_Click(object sender = null, EventArgs e = null) {
            UpdateDatabase();

            if (!CustomPoolEnabled) {
                if (textBoxBitcoinAddress.Text != "" && !ValidateBitcoinAddress())
                    return;
                if (textBoxEthereumAddress.Text != "" && !ValidateEthereumAddress())
                    return;
                if (textBoxMoneroAddress.Text != "" && !ValidateMoneroAddress())
                    return;
                if (textBoxPascalAddress.Text != "" && !ValidatePascalAddress())
                    return;
                if (textBoxLbryAddress.Text != "" && !ValidateLbryAddress())
                    return;
                if (textBoxRigID.Text != "" && !ValidateRigID())
                    if (textBoxRigID.Text != "" && !ValidateRigID())
                        if (textBoxRigID.Text != "" && !ValidateRigID())
                    return;
                if (textBoxBitcoinAddress.Text == "" 
                    && textBoxEthereumAddress.Text == ""
                    && textBoxMoneroAddress.Text == ""
                    && textBoxPascalAddress.Text == ""
                    && textBoxLbryAddress.Text == "") {
                    MessageBox.Show("Please enter at least one valid wallet address.", appName, MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    tabControlMainForm.TabIndex = 1;
                    return;
                }
            }
            var enabled = false;
            foreach (var control in checkBoxGPUEnableArray)
                enabled = enabled || control.Checked;
            if (!enabled) {
                MessageBox.Show("Please enable at least one device.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                tabControlMainForm.TabIndex = 0;
                return;
            }

            tabControlMainForm.Enabled = buttonStart.Enabled = false;

            if (appState == ApplicationGlobalState.Idle) {
                foreach (var device in mDevices) {
                    device.ClearShares();
                    labelGPUSharesArray[device.DeviceIndex].Text = "0";
                }

                mPrimaryStratum = null;
                mSecondaryStratum = null;
                mActiveMiners.Clear();

                mDevFeeMode = false;
                LaunchMiners();
                if (mPrimaryStratum == null || !mActiveMiners.Any()) {
                    MessageBox.Show("Failed to launch miner.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                } else {
                    appState = ApplicationGlobalState.Mining;
                    tabControlMainForm.SelectedIndex = 0;
                    timerDevFee.Interval = 15 * 60 * 1000;
                    timerDevFee.Enabled = true;
                    mStartTime = DateTime.Now;
                    mDevFeeModeStartTime = DateTime.Now;
                    try { using (var file = new System.IO.StreamWriter(mAppStateFileName, false)) file.WriteLine("Mining"); } catch (Exception) { }
                }
            } else if (appState == ApplicationGlobalState.Mining) {
                timerDevFee.Enabled = false;
                StopMiners();
                appState = ApplicationGlobalState.Idle;
                try { using (var file = new System.IO.StreamWriter(mAppStateFileName, false)) file.WriteLine("Idle"); } catch (Exception) { }
            }

            UpdateStatsWithShortPolling();
            //UpdateStatsWithLongPolling();
            UpdateControls();
            timerCurrencyStatUpdates.Interval = 100;
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void UpdateControls() {
            if (appState == ApplicationGlobalState.Initializing)
                return;

            try {
                buttonStart.Text = appState == ApplicationGlobalState.Mining ? "Stop" : "Start";
                buttonBenchmark.Enabled = false;

                groupBoxCoinsToMine.Enabled = appState == ApplicationGlobalState.Idle && !CustomPoolEnabled;
                groupBoxPoolPriorities.Enabled = appState == ApplicationGlobalState.Idle && !CustomPoolEnabled;
                groupBoxPoolParameters.Enabled = appState == ApplicationGlobalState.Idle && !CustomPoolEnabled;
                groupBoxWalletAddresses.Enabled = appState == ApplicationGlobalState.Idle && !CustomPoolEnabled;
                groupBoxAutomation.Enabled = appState == ApplicationGlobalState.Idle;
                groupBoxHadrwareAcceleration.Enabled = appState == ApplicationGlobalState.Idle;
                foreach (var control in checkBoxGPUEnableArray)
                    control.Enabled = appState == ApplicationGlobalState.Idle;
                groupBoxCustmPool0.Enabled = appState == ApplicationGlobalState.Idle;
                groupBoxCustmPool1.Enabled = appState == ApplicationGlobalState.Idle;
                groupBoxCustmPool2.Enabled = appState == ApplicationGlobalState.Idle;
                groupBoxCustmPool3.Enabled = appState == ApplicationGlobalState.Idle;

                textBoxCustomPool0Host.Enabled = textBoxCustomPool0Login.Enabled = textBoxCustomPool0Password.Enabled = comboBoxCustomPool0Algorithm.Enabled = comboBoxCustomPool0SecondaryAlgorithm.Enabled = numericUpDownCustomPool0Port.Enabled = checkBoxCustomPool0Enable.Checked;
                textBoxCustomPool1Host.Enabled = textBoxCustomPool1Login.Enabled = textBoxCustomPool1Password.Enabled = comboBoxCustomPool1Algorithm.Enabled = comboBoxCustomPool1SecondaryAlgorithm.Enabled = numericUpDownCustomPool1Port.Enabled = checkBoxCustomPool1Enable.Checked;
                textBoxCustomPool2Host.Enabled = textBoxCustomPool2Login.Enabled = textBoxCustomPool2Password.Enabled = comboBoxCustomPool2Algorithm.Enabled = comboBoxCustomPool2SecondaryAlgorithm.Enabled = numericUpDownCustomPool2Port.Enabled = checkBoxCustomPool2Enable.Checked;
                textBoxCustomPool3Host.Enabled = textBoxCustomPool3Login.Enabled = textBoxCustomPool3Password.Enabled = comboBoxCustomPool3Algorithm.Enabled = comboBoxCustomPool3SecondaryAlgorithm.Enabled = numericUpDownCustomPool3Port.Enabled = checkBoxCustomPool3Enable.Checked;

                if ((string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool0Algorithm.SelectedIndex] != "Ethash") comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex = 0;
                if ((string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool1Algorithm.SelectedIndex] != "Ethash") comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex = 0;
                if ((string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool2Algorithm.SelectedIndex] != "Ethash") comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex = 0;
                if ((string)comboBoxCustomPool0Algorithm.Items[comboBoxCustomPool3Algorithm.SelectedIndex] != "Ethash") comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex = 0;

                textBoxCustomPool0SecondaryHost.Enabled = textBoxCustomPool0SecondaryLogin.Enabled = textBoxCustomPool0SecondaryPassword.Enabled = numericUpDownCustomPool0SecondaryPort.Enabled = checkBoxCustomPool0Enable.Checked && comboBoxCustomPool0SecondaryAlgorithm.SelectedIndex != 0;
                textBoxCustomPool1SecondaryHost.Enabled = textBoxCustomPool1SecondaryLogin.Enabled = textBoxCustomPool1SecondaryPassword.Enabled = numericUpDownCustomPool1SecondaryPort.Enabled = checkBoxCustomPool1Enable.Checked && comboBoxCustomPool1SecondaryAlgorithm.SelectedIndex != 0;
                textBoxCustomPool2SecondaryHost.Enabled = textBoxCustomPool2SecondaryLogin.Enabled = textBoxCustomPool2SecondaryPassword.Enabled = numericUpDownCustomPool2SecondaryPort.Enabled = checkBoxCustomPool2Enable.Checked && comboBoxCustomPool2SecondaryAlgorithm.SelectedIndex != 0;
                textBoxCustomPool3SecondaryHost.Enabled = textBoxCustomPool3SecondaryLogin.Enabled = textBoxCustomPool3SecondaryPassword.Enabled = numericUpDownCustomPool3SecondaryPort.Enabled = checkBoxCustomPool3Enable.Checked && comboBoxCustomPool3SecondaryAlgorithm.SelectedIndex != 0;

                tabControlMainForm.Enabled = buttonStart.Enabled = true;
            } catch (Exception ex) {
                Logger("Exception in UpdateControls(): " + ex.Message + ex.StackTrace);
            }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void timerCurrencyStatUpdates_Tick(object sender, EventArgs e) {
            try {
                if (appState == ApplicationGlobalState.Mining && (DateTime.Now - mStartTime).Seconds < 180) {
                    timerCurrencyStatUpdates.Interval = 20 * 1000;
                } else {
                    timerCurrencyStatUpdates.Interval = 5 * 60 * 1000;
                }
                UpdateStatsWithLongPolling();
            } catch (Exception ex) {
                Logger("Exception in timerCurrencyStatUpdates_Tick(): " + ex.Message + ex.StackTrace);
            }
        }

        private void buttonPoolPrioritiesUp_Click(object sender, EventArgs e) {
            var selectedIndex = listBoxPoolPriorities.SelectedIndex;
            if (selectedIndex > 0) {
                listBoxPoolPriorities.Items.Insert(selectedIndex - 1, listBoxPoolPriorities.Items[selectedIndex]);
                listBoxPoolPriorities.Items.RemoveAt(selectedIndex + 1);
                listBoxPoolPriorities.SelectedIndex = selectedIndex - 1;
                UpdateStatsWithLongPolling();
            }
        }

        private void buttonPoolPrioritiesDown_Click(object sender, EventArgs e) {
            var selectedIndex = listBoxPoolPriorities.SelectedIndex;
            if ((selectedIndex < listBoxPoolPriorities.Items.Count - 1) & (selectedIndex != -1)) {
                listBoxPoolPriorities.Items.Insert(selectedIndex + 2, listBoxPoolPriorities.Items[selectedIndex]);
                listBoxPoolPriorities.Items.RemoveAt(selectedIndex);
                listBoxPoolPriorities.SelectedIndex = selectedIndex + 1;
                UpdateStatsWithLongPolling();
            }
        }

        private void buttonViewBalancesAtNiceHash_Click(object sender, EventArgs e) {
            if (ValidateBitcoinAddress())
                System.Diagnostics.Process.Start("https://www.nicehash.com/miner/" + textBoxBitcoinAddress.Text);
        }

        private void tabControlMainForm_SelectedIndexChanged(object sender, EventArgs e) {
        }

        private void buttonEthereumBalance_Click(object sender, EventArgs e) {
            if (!ValidateEthereumAddress())
                return;
            foreach (string poolName in listBoxPoolPriorities.Items)
                if (poolName == "Nanopool") {
                    System.Diagnostics.Process.Start("https://eth.nanopool.org/account/" + textBoxEthereumAddress.Text);
                    return;
                } else if (poolName == "DwarfPool") {
                    System.Diagnostics.Process.Start("https://dwarfpool.com/eth/address?wallet=" + textBoxEthereumAddress.Text);
                    return;
                } else if (poolName == "ethermine.org") {
                    System.Diagnostics.Process.Start("https://ethermine.org/miners/" + textBoxEthereumAddress.Text);
                    return;
                } else if (poolName == "ethpool.org") {
                    System.Diagnostics.Process.Start("https://ethpool.org/miners/" + textBoxEthereumAddress.Text);
                    return;
                }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void timerDevFee_Tick(object sender, EventArgs e) {
            try {
                if (appState != ApplicationGlobalState.Mining) {
                    timerDevFee.Enabled = false;
                } else if (mDevFeeMode) {
                    labelCurrentPool.Text = "Switching...";
                    tabControlMainForm.Enabled = buttonStart.Enabled = false;
                    StopMiners();
                    mDevFeeMode = false;
                    timerDevFee.Interval = (int)((double)mDevFeeDurationInSeconds * ((double)(100 - mDevFeePercentage) / mDevFeePercentage) * 1000);
                    LaunchMiners();
                    if (mActiveMiners.Count() == 0 || mPrimaryStratum == null) {
                        mDevFeeMode = true;
                        timerDevFee.Interval = 1000;
                    }
                } else {
                    labelCurrentPool.Text = "Switching...";
                    tabControlMainForm.Enabled = buttonStart.Enabled = false;
                    StopMiners();
                    mDevFeeMode = true;
                    mDevFeeModeStartTime = DateTime.Now;
                    timerDevFee.Interval = mDevFeeDurationInSeconds * 1000;
                    LaunchMiners();
                    if (mActiveMiners.Count() == 0 || mPrimaryStratum == null) {
                        mDevFeeMode = false;
                        timerDevFee.Interval = 1000;
                    }
                }

                UpdateStatsWithLongPolling();
                tabControlMainForm.Enabled = buttonStart.Enabled = true;
            } catch (Exception ex) {
                Logger("Exception in timerDevFee_Tick(): " + ex.Message + ex.StackTrace);
            }
        }

        private void radioButtonMonero_CheckedChanged(object sender, EventArgs e) {
        }

        private void radioButtonEthereum_CheckedChanged(object sender, EventArgs e) {
        }

        private void radioButtonMostProfitable_CheckedChanged(object sender, EventArgs e) {
        }

        private void radioButtonZcash_CheckedChanged(object sender, EventArgs e) {
        }

        private void radioButtonLbry_CheckedChanged(object sender, EventArgs e) {
        }

        private void radioButtonPascal_CheckedChanged(object sender, EventArgs e) {
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void timerWatchdog_Tick(object sender, EventArgs e) {
            try {
                if (appState == ApplicationGlobalState.Mining && mActiveMiners.Any())
                    foreach (var miner in mActiveMiners)
                        miner.KeepAlive();
            } catch (Exception ex) {
                Logger("Exception in timerWatchdog_Tick(): " + ex.Message + ex.StackTrace);
            }
        }

        private void buttonMoneroBalance_Click(object sender, EventArgs e) {
            if (!ValidateMoneroAddress())
                return;
            foreach (string poolName in listBoxPoolPriorities.Items)
                if (poolName == "Nanopool") {
                    System.Diagnostics.Process.Start("https://xmr.nanopool.org/account/" + textBoxMoneroAddress.Text);
                    return;
                } else if (poolName == "DwarfPool") {
                    System.Diagnostics.Process.Start("https://dwarfpool.com/xmr/address?wallet=" + textBoxMoneroAddress.Text);
                    return;
                } else if (poolName == "mineXMR.com") {
                    System.Diagnostics.Process.Start("http://minexmr.com/");
                    return;
                }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        private void timerUpdateLog_Tick(object sender, EventArgs e) {
            try {
                UpdateLog();
            } catch (Exception ex) {
                Logger("Exception in timerUpdateLog_Tick(): " + ex.Message + ex.StackTrace);
            }
        }

        private void checkBoxLaunchAtStartup_CheckedChanged(object sender, EventArgs e) {
            try {
                var process = new System.Diagnostics.Process();
                var startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                if (checkBoxLaunchAtStartup.Checked)
                    startInfo.Arguments = "/C schtasks /create /sc onlogon /tn GatelessGateSharp /rl highest /tr \"" + Application.ExecutablePath + "\"";
                else
                    startInfo.Arguments = "/C schtasks /delete /f /tn GatelessGateSharp";
                process.StartInfo = startInfo;
                process.Start();
            } catch (Exception) {
                MessageBox.Show("Failed to complete the operation.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkBoxGPU0Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU1Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU2Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU3Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU4Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU5Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU6Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void checkBoxGPU7Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateStatsWithShortPolling();
        }

        private void labelGPU0ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU0MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU0Enable.Checked = !checkBoxGPU0Enable.Checked;
        }

        private void labelGPU1ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU1MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU1Enable.Checked = !checkBoxGPU1Enable.Checked;
        }

        private void labelGPU2ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU2MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU2Enable.Checked = !checkBoxGPU2Enable.Checked;
        }

        private void labelGPU3Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU3MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU3Enable.Checked = !checkBoxGPU3Enable.Checked;
        }

        private void labelGPU4ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU4MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU4Enable.Checked = !checkBoxGPU4Enable.Checked;
        }

        private void labelGPU5ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU5MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU5Enable.Checked = !checkBoxGPU5Enable.Checked;
        }

        private void labelGPU6ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU6MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU6Enable.Checked = !checkBoxGPU6Enable.Checked;
        }

        private void labelGPU7ID_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Vendor_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Name_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Speed_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Shares_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Activity_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Temp_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7Fan_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7CoreClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void labelGPU7MemoryClock_Click(object sender, EventArgs e) {
            if (appState == ApplicationGlobalState.Idle) checkBoxGPU7Enable.Checked = !checkBoxGPU7Enable.Checked;
        }

        private void buttonClearLog_Click(object sender, EventArgs e) {
            Utilities.FixFPU();
            richTextBoxLog.Clear();
        }

        private void buttonOpenLog_Click(object sender, EventArgs e) {
            System.Diagnostics.Process.Start(logFileName);
        }

        private void timerAutoStart_Tick(object sender, EventArgs e) {
            timerAutoStart.Enabled = false;
            buttonStart_Click();
        }

        private void checkBoxCustomPool0Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void checkBoxCustomPool1Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void checkBoxCustomPool2Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void checkBoxCustomPool3Enable_CheckedChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool0Algorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool0SecondaryAlgorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool1Algorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool1SecondaryAlgorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool2Algorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool2SecondaryAlgorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool3Algorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void comboBoxCustomPool3SecondaryAlgorithm_SelectedIndexChanged(object sender, EventArgs e) {
            UpdateControls();
        }

        private void buttonViewPascalBalance_Click(object sender, EventArgs e) {

        }

        private void buttonLbryBalance_Click(object sender, EventArgs e) {

        }
    }
}
