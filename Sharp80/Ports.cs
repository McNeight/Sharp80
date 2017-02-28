/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3

using System;

namespace Sharp80
{
    internal sealed class PortSet
    {
        private const int NUM_PORTS = 0x100;

        Computer computer;
        InterruptManager IntMgr;

        private byte[] ports = new byte[NUM_PORTS];
        private byte[] lastOUT = new byte[NUM_PORTS];
        private bool noDrives = true;

        public PortSet(Computer Computer, InterruptManager InterruptManager)
        {
            computer = Computer;
            IntMgr = InterruptManager;
            Reset();
        }

        public bool NoDrives
        {
            get { return noDrives; }
            set
            {
                if (noDrives != value)
                {
                    if (noDrives)
                        ports[0xF0] = 0xFF;
                    else
                        ports[0xF0] = 0x80;

                    noDrives = value;
                }
            }
        }
        public void Reset()
        {
            for (int i = 0; i < NUM_PORTS; i++)
            {
                ports[i] = 0xFF;
                lastOUT[i] = 0x00;
            }

            ports[0x50] = 0x00;
            ports[0x51] = 0x00;
            ports[0x52] = 0x00;
            ports[0x53] = 0x00;
            ports[0x54] = 0x00;
            ports[0x55] = 0x00;
            ports[0x56] = 0x00;
            ports[0x57] = 0x00;
            ports[0x58] = 0x00;
            ports[0x59] = 0x00;
            ports[0x5A] = 0x00;
            ports[0x5B] = 0x00;
            ports[0x5C] = 0x00;
            ports[0x5D] = 0x00;
            ports[0x5E] = 0x00;
            ports[0x5F] = 0x32;
            ports[0x68] = 0x59;
            ports[0x69] = 0x1E;
            ports[0x6A] = 0x16;
            ports[0x6B] = 0x05;
            ports[0x6C] = 0x24;
            ports[0x6D] = 0x04; 
            ports[0x71] = 0x00;
            ports[0xE0] = 0xFB;
            ports[0xE4] = 0x00;                // FDC Status
            ports[0xEC] = 0x12;                // Reset clock / Various controls
            ports[0xF0] = 0x80;                
            ports[0xF1] = 0x00;            
            ports[0xF8] = 0x3F;
            ports[0xF9] = 0x3F;
            ports[0xFA] = 0x3F;
            ports[0xFB] = 0x3F;
            ports[0xFC] = 0x89;
            ports[0xFD] = 0x89;
            ports[0xFE] = 0x89;
            ports[0xFF] = 0x89;                 // cassette, not ready
        }

        public byte this[byte PortNumber]
        {
            get
            {
                switch (PortNumber)
                {
                    case 0xE0:
                    case 0xE1:
                    case 0xE2:
                    case 0xE3:
                        ports[PortNumber] = IntMgr.WrIntMaskReg;
                        break;
                    case 0xEC:
                    case 0xED:
                    case 0xEE:
                    case 0xEF:
                        IntMgr.ECin();
                        break;
                    case 0xE4:
                        IntMgr.E4in();
                        break;
                    case 0xF0:
                        if (NoDrives)
                            ports[0xF0] = 0xFF; // cassette system
                        else
                            computer.FloppyController.FdcIoEvent(PortNumber, 0, false);
                        break;
                    case 0xF1:
                    case 0xF2:
                    case 0xF3:
                        computer.FloppyController.FdcIoEvent(PortNumber, 0, false);
                        break;
                    case 0xFF:
                        ports[0xFF] &= 0xFC;
                        break;
                }
                Log.LogToDebug("Reading Port " + PortNumber.ToHexString() + " [" + ports[PortNumber].ToHexString() + "]");
                return ports[PortNumber];
            }
            set
            {
                lastOUT[PortNumber] = value;

                switch (PortNumber)
                {
                    case 0xE0:
                    case 0xE1:
                    case 0xE2:
                    case 0xE3:
                        IntMgr.WrIntMaskReg = value;
                        break;
                    case 0xEC:
                    case 0xED:
                    case 0xEE:
                    case 0xEF:
                        // Update double width and Kanji character settings
                        computer.SetVideoMode(value.IsBitSet(2), !value.IsBitSet(3));
                        break;
                    case 0xE4:
                    case 0xE5:
                    case 0xE6:
                    case 0xE7:
                    case 0xF0:
                    case 0xF1:
                    case 0xF2:
                    case 0xF3:
                    case 0xF4:
                        computer.FloppyController.FdcIoEvent(PortNumber, value, true);
                        break;
                    default:
                        break;
                }
                Log.LogToDebug("Writing Port " + PortNumber.ToHexString() + " [" + value.ToHexString() + "]");
                return;
            }
        }
        /// <summary>
        /// Used to avoid side effects
        /// </summary>
        public void SetPortDirect(byte B, byte PortNum)
        {
            ports[PortNum] = B;
        }
        /// <summary>
        /// Used to avoid side effects
        /// </summary>
        public byte GetPortDirect(byte PortNum)
        {
            return ports[PortNum];
        }
        public byte CassetteOut()
        {
            return lastOUT[0xFF];
        }
      
        // SNAPSHOTS

        public void Serialize(System.IO.BinaryWriter Writer)
        {
            Writer.Write(ports);
            Writer.Write(lastOUT);
            Writer.Write(NoDrives);
        }
        public void Deserialize(System.IO.BinaryReader Reader)
        {
            Array.Copy(Reader.ReadBytes(NUM_PORTS), ports, NUM_PORTS);
            Array.Copy(Reader.ReadBytes(NUM_PORTS), lastOUT, NUM_PORTS);
            NoDrives = Reader.ReadBoolean();
        }
    }
}
