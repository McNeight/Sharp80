﻿/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3. See license.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sharp80.Z80
{
    public class Instruction
    {
        public const ushort TICKS_PER_TSTATE = 1000;

        private readonly byte[] op = new byte[4];

        private string operand0 = null;
        private string operand1 = null;
        private string operand2 = null;
        private string postMnemonic;
        private int? numOperands = null;

        internal Action Execute { get; private set; }

        public byte RIncrement { get; private set; }
        public bool IsPrefix { get; private set; }
        public byte TStates { get; private set; }
        public byte TStatesAlt { get; private set; }
        public ushort Ticks { get; private set; }
        public ushort TicksWithExtra { get; private set; }
        public uint Signature { get; private set; }
        public uint PaddedSig { get; private set; }

        public Func<IReadOnlyList<byte>, ushort, string> FullName { get; private set; }
        public Func<IReadOnlyList<byte>, ushort, string> AssemblableName { get; private set; }

        public string Name { get; private set; }
        public string Mnemonic { get; private set; }
        
        public byte Size { get; private set; }
        public byte OpcodeSize { get; private set; }
        public byte OpcodeCoreSize { get; private set; }
        
        public Instruction(string Name, byte TStates, Action Exec, byte Op0, byte? Op1 = null, byte? Op3 = null)
        {
            this.Name = Name;
            Execute = Exec;

            Mnemonic = Name.FirstText();

            op[0] = Op0;
            op[1] = Op1 ?? 0x00;
            op[2] = 0x00;
            op[3] = Op3 ?? 0x00;

            OpcodeCoreSize = (byte)(1 + (Op1.HasValue ? 1 : 0));
            OpcodeSize = (byte)(OpcodeCoreSize + (Op3.HasValue ? 1 : 0));

            Size = OpcodeSize;
            switch (OpcodeSize)
            {
                case 1:
                    Signature = Op0;
                    PaddedSig = (uint)(op[0] << 16);
                    break;
                case 2:
                    Signature = (uint)((op[0] << 8) | op[1]);
                    PaddedSig = Signature << 8;
                    break;
                default:
                    Signature = (uint)((op[0] << 16) | (op[1] << 8) | (op[3]));
                    PaddedSig = Signature;
                    break;
            }

            bool hasDisp = Name.Contains("+d");
            bool hasLiteral16 = Name.Contains("NN");
            bool hasLiteral8 = !hasLiteral16 && Name.Contains(" N") && !Name.Contains(" NZ") & !Name.Contains(" NC");
            bool hasRelJump = Name.Contains(" e");
            bool hasPortRefNum = Name.Contains("(N)");

            if (hasDisp)
                Size++;
            if (hasLiteral16)
                Size += 2;
            if (hasLiteral8)
                Size++;
            if (hasRelJump)
                Size++;
            if (hasPortRefNum)
                Size++;

            bool hasReplaceableTokens = hasDisp || hasLiteral8 || hasLiteral16 || hasRelJump || hasPortRefNum;

            this.TStates = TStates;
            TStatesAlt = 0;
            Ticks = (ushort)(TStates * TICKS_PER_TSTATE);
            TicksWithExtra = Ticks; 
            RIncrement = 1;

            if ((op[0] == 0xDD) || (op[0] == 0xFD) || (op[0] == 0xCB) || (op[0] == 0xED))
                RIncrement++;

            Debug.Assert(!hasReplaceableTokens || Size > OpcodeSize);
            Debug.Assert(!(Op1 is null) || Op3 is null);
            Debug.Assert(Op1 is null || Size >= 2);
            Debug.Assert(Op3 is null || Size == 4);

            postMnemonic = Name.Substring(Mnemonic.Length);

            InitNameFn(hasReplaceableTokens);
        }
        public Instruction AsPrefix()
        {
            Debug.Assert(Op0 == 0xDD || Op0 == 0xFD);
            IsPrefix = true;
            RIncrement = 1;
            return this;
        }
        public Instruction WithTStatesAlt(byte AddedTStates)
        {
            TStatesAlt = AddedTStates;
            TicksWithExtra = (ushort)((TStates + TStatesAlt) * TICKS_PER_TSTATE);
            return this;
        }
        public int NumOperands
        {
            get
            {
                if (!numOperands.HasValue)
                {
                    if (String.IsNullOrEmpty(Operand0))
                        numOperands = 0;
                    else if (String.IsNullOrEmpty(Operand1))
                        numOperands = 1;
                    else if (String.IsNullOrEmpty(Operand2))
                        numOperands = 2;
                    else
                        numOperands = 3;
                }
                return numOperands.Value;
            }
        }
        public byte Op0 => op[0];
        public byte Op1 => op[1];
        public byte Op3 => op[3];

        internal string GetOperand(int Index)
        {
            switch (Index)
            {
                case 0: return Operand0;
                case 1: return Operand1;
                case 2: return Operand2;
                default: return String.Empty;
            }
        }

        private string Operand0
        {
            get
            {
                if (operand0 != null)
                    return operand0;

                var s = Name.Substring(Mnemonic.Length);
                var commaLoc = s.IndexOf(',');

                if (commaLoc < 0)
                    return operand0 = s.Trim();
                else
                    return operand0 = s.Substring(0, commaLoc).Trim();
            }
        }
        private string Operand1
        {
            get
            {
                if (operand1 != null)
                    return operand1;

                var s = Name.Substring(Mnemonic.Length);
                var commaLoc = s.IndexOf(',');

                if (commaLoc < 0)
                    return operand1 = String.Empty;
                else
                    return operand1 = s.Substring(commaLoc + 1).Trim();
            }
        }
        private string Operand2
        {
            get
            {
                // Only used in some undocumented compound instructions
                if (operand2 != null)
                    return operand2;

                var s = Name.Substring(Mnemonic.Length);
                var commaLoc = s.IndexOf(',');

                if (commaLoc < 0)
                    return operand2 = String.Empty;
                else
                {
                    commaLoc = s.IndexOf(',', commaLoc + 1);
                    if (commaLoc < 0)
                        return operand2 = String.Empty;
                    else
                        return operand2 = s.Substring(commaLoc + 1).Trim();
                }
            }
        }

        // RENDERING

        private string NameNN(IReadOnlyList<byte> Memory, ushort PC) => Mnemonic + postMnemonic.Replace("NN", Lib.CombineBytes(Memory[PC.Offset(OpcodeCoreSize)], Memory[PC.Offset(OpcodeCoreSize + 1)]).ToHexString());
        private string NameMM(IReadOnlyList<byte> Memory, ushort PC) => Mnemonic + postMnemonic.Replace("(N)", "(" + (Memory[PC.Offset(Size - 1)]).ToHexString() + ")");
        private string NameN(IReadOnlyList<byte> Memory, ushort PC)  => Mnemonic + postMnemonic.Replace(" N", " " + Memory[PC.Offset(Size - 1)].ToHexString());
        private string NameD(IReadOnlyList<byte> Memory, ushort PC)  => Mnemonic + postMnemonic.Replace("+d", Memory[PC.Offset(OpcodeCoreSize)].ToTwosCompHexString());
        private string NameE(IReadOnlyList<byte> Memory, ushort PC)  => Mnemonic + postMnemonic.Replace(" e", " " + PC.Offset(Size + Memory[PC.Offset(OpcodeCoreSize)].TwosComp()).ToHexString());
        private string NameDN(IReadOnlyList<byte> Memory, ushort PC) => Mnemonic + postMnemonic.Replace("+d", Memory[PC.Offset(OpcodeCoreSize)].ToTwosCompHexString()).Replace(" N", " " + Memory[PC.Offset(Size - 1)].ToHexString());

        private string NameA(IReadOnlyList<byte> Memory, ushort PC)   => ("\t" + Mnemonic + "\t" + postMnemonic).Replace("\t ", "\t");
        private string NameNNA(IReadOnlyList<byte> Memory, ushort PC) => ("\t" + Mnemonic + "\t" + postMnemonic).Replace("NN", Lib.CombineBytes(Memory[PC.Offset(OpcodeCoreSize)], Memory[PC.Offset(OpcodeCoreSize + 1)]).ToHexString() + "H").Replace("\t ", "\t");
        private string NameMMA(IReadOnlyList<byte> Memory, ushort PC) => ("\t" + Mnemonic + "\t" + postMnemonic).Replace("(N)", "(" + (Memory[PC.Offset(Size - 1)]).ToHexString() + "H)").Replace("\t ", "\t");
        private string NameNA(IReadOnlyList<byte> Memory, ushort PC)  => ("\t" + Mnemonic + "\t" + postMnemonic).Replace(" N", " " + Memory[PC.Offset(Size - 1)].ToHexString() + "H").Replace("\t ", "\t");
        private string NameDA(IReadOnlyList<byte> Memory, ushort PC)  => ("\t" + Mnemonic + "\t" + postMnemonic).Replace("+d", Memory[PC.Offset(OpcodeCoreSize)].ToTwosCompHexString() + "H").Replace("\t ", "\t");
        private string NameEA(IReadOnlyList<byte> Memory, ushort PC)  => ("\t" + Mnemonic + "\t" + postMnemonic).Replace(" e", " " + PC.Offset(Size + Memory[PC.Offset(OpcodeCoreSize)].TwosComp()).ToHexString() + "H").Replace("\t ", "\t");
        private string NameDNA(IReadOnlyList<byte> Memory, ushort PC) => ("\t" + Mnemonic + "\t" + postMnemonic).Replace("+d", Memory[PC.Offset(OpcodeCoreSize)].ToTwosCompHexString() + "H").Replace(" N", " " + Memory[PC.Offset(Size - 1)].ToHexString() + "H").Replace("\t ", "\t");

        public override string ToString() => Name;

        private void InitNameFn(bool HasReplaceableTokens)
        {
            if (HasReplaceableTokens)
            {
                if (postMnemonic.Contains("NN"))
                {
                    FullName = NameNN;
                    AssemblableName = NameNNA;
                }
                else if (postMnemonic.Contains("(N)"))
                {
                    FullName = NameMM;
                    AssemblableName = NameMMA;
                }
                else if (postMnemonic.Contains(" e"))
                {
                    FullName = NameE;
                    AssemblableName = NameEA;
                }
                else if (postMnemonic.Contains(" N"))
                {
                    if (postMnemonic.Contains("+d"))
                    {
                        FullName = NameDN;
                        AssemblableName = NameDNA;
                    }
                    else
                    {
                        FullName = NameN;
                        AssemblableName = NameNA;
                    }
                }
                else if (postMnemonic.Contains("+d"))
                {
                    FullName = NameD;
                    AssemblableName = NameDA;
                }
            }
            else
            {
                FullName = (a, b) => Name;
                if (postMnemonic.Length == 0)
                    AssemblableName = (a, b) => "\t" + Name;
                else if (Mnemonic == "RST")
                    AssemblableName = (a, b) => "\t" + Mnemonic + "\t" + postMnemonic + "H";
                else
                    AssemblableName = NameA;
            }
            Debug.Assert(!(FullName is null));
            Debug.Assert(!(AssemblableName is null));
        }
    }
}
