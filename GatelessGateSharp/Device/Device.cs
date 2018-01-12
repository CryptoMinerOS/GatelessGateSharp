﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cloo;



namespace GatelessGateSharp
{
    public class Device
    {
        private int mDeviceIndex;
        private int mAcceptedShares;
        private int mRejectedShares;
        private ComputeContext mContext = null;
        private System.Threading.Mutex mMutex = new System.Threading.Mutex();
        private List<ComputeDevice> mDeviceList;

        public virtual String GetVendor() { return ""; }
        public virtual String GetName() { return ""; }
        public virtual long GetMemorySize() { return 0; }
        public virtual long GetMaxComputeUnits() { return 0; }

        public int DeviceIndex { get { return mDeviceIndex; } }
        public long MemorySize { get { return GetMemorySize(); } }
        public int AcceptedShares { get { return mAcceptedShares; } }
        public int RejectedShares { get { return mRejectedShares; } }

        public Device(int aDeviceIndex)
        {
            mDeviceIndex = aDeviceIndex;
            mAcceptedShares = 0;
            mRejectedShares = 0;
        }

        public int IncrementAcceptedShares()
        {
            return ++mAcceptedShares;
        }

        public int IncrementRejectedShares()
        {
            return ++mRejectedShares;
        }

        public void ClearShares()
        {
            mAcceptedShares = 0;
            mRejectedShares = 0;
        }
    }
}
