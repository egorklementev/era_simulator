using ERASimulator.Errors;
using System;
using System.Text;

namespace ERASimulator.Modules
{
    class Simulator
    {
        private readonly uint[] regs;        
        private readonly byte[] binary;
        private readonly byte[] memory;

        private const int FP = 28, SP = 29, SB = 30, PC = 31;
        private const int OK = 0, STOP = 1, MEMORY_OUT_OF_BOUND = 10, WRONG_REGISTER = 11;

        private readonly string[] commands;
        private readonly string[] registers;

        public static StringBuilder executionTrace = new StringBuilder();
        public static StringBuilder printTrace = new StringBuilder();

        public Simulator(byte[] binaryCode)
        {
            binary = binaryCode;
            memory = new byte[Program.bytesToAllocate];
            regs = new uint[32];
            commands = new string[] 
            {
                "STOP/SKIP/PRINT",
                "LD",
                "LDA/LDC",
                "ST",
                "MOV",
                "ADD",
                "SUB",
                "ASR",
                "ASL",
                "OR",
                "AND",
                "XOR",
                "LSL",
                "LSR",
                "CND",
                "CBR"
            };
            registers = new string[32];
            for (int i = 0; i < 28; i++)
            {
                registers[i] = i.ToString();
            }
            registers[28] = "FP";
            registers[29] = "SP";
            registers[30] = "SB";
            registers[31] = "PC";
            executionTrace = new StringBuilder();
            printTrace = new StringBuilder();
        }

        public string Simulate()
        {
            // Binary file arrangement:
            // 2 bytes - junk
            // 4 bytes - static data address
            // 4 bytes - static data length
            // 4 bytes - program data address
            // 4 bytes - program data length
            // X bytes - program/static/junk bytes

            uint staticDataAddress = LoadLWordFromBinary(2);
            uint staticDataLength = LoadLWordFromBinary(6) * 2;
            uint programDataAddress = LoadLWordFromBinary(10);
            uint programDataLength = LoadLWordFromBinary(14) * 2;

            // Load all static data before program data
            regs[SB] = 0;
            regs[PC] = staticDataLength;
            for (int i = 0; i < staticDataLength; i++)
            {
                memory[regs[SB] + i] = binary[staticDataAddress + i];
            }
            for (int i = 0; i < programDataLength; i++)
            {
                memory[regs[PC] + i] = binary[programDataAddress + i];
            }
            regs[SP] = staticDataLength + programDataLength;

            // Execute the code
            byte[] currentCommand = new byte[] 
            { 
                memory[regs[PC] + 0], 
                memory[regs[PC] + 1]
            };
            regs[PC] += 2;
            int status;
            long commandNum = 0;
            while (true) 
            {
                commandNum++;
                status = ExecuteCommand(currentCommand);
                if (status != OK && status != STOP)
                {
                    throw new SimulationErrorException("Execution stopped abnormally! Status: " + status.ToString());
                }
                if (status == STOP) break;

                currentCommand[0] = memory[regs[PC] + 0];
                currentCommand[1] = memory[regs[PC] + 1];
                regs[PC] += 2;
            }

            // Construct a memory dump file
            if (!Program.noDump)
            {
                StringBuilder memoryDump = new StringBuilder();

                memoryDump.Append("Exited normally with the code ").Append(status).Append(".\r\n\r\n");
                for (int i = 0; i < 28; i++)
                {
                    memoryDump.Append("R").Append(i).Append(" = ").Append(regs[i]).Append("\r\n");
                }
                memoryDump.Append("FP").Append(" = ").Append(regs[FP]).Append("\r\n");
                memoryDump.Append("SP").Append(" = ").Append(regs[SP]).Append("\r\n");
                memoryDump.Append("SB").Append(" = ").Append(regs[SB]).Append("\r\n");
                memoryDump.Append("PC").Append(" = ").Append(regs[PC]).Append("\r\n");
                memoryDump.Append("\r\n");
                int width = 32;
                for (int i = 0; i < memory.Length; i+=width)
                {
                    byte[] subset = new byte[width];
                    for (int j = 0; j < width; j++)
                    {
                        subset[j] = memory[i + j];
                    }
                    memoryDump.Append(BitConverter.ToString(subset).Replace("-", " ")).Append("\r\n");            
                }

                if (Program.showTrace)
                {
                    return memoryDump.ToString() + "\r\n" + executionTrace.ToString();
                }
                else
                {
                    return memoryDump.ToString();
                }
            }
            return "";
        }

        private int ExecuteCommand(byte[] command)
        {
            int format = command[0] >> 6;
            int opCode = (command[0] & 0x3c) >> 2;
            int regi = ((command[0] & 0x03) << 3) | (command[1] >> 5);
            int regj = (command[1] & 0x1f);

            if (Program.showTrace) 
            {
                executionTrace.Append(
                    "[" + regs[PC] + "] " + 
                    commands[opCode] + ": " + 
                    registers[regi] + ", " + 
                    registers[regj] + "  (" +
                    regs[regi] + "  " +
                    regs[regj] + ")"
                    );
                if (opCode != 2 || format != 0)
                    executionTrace.Append("\n");
            }
            
            if (regj == PC)
                return WRONG_REGISTER;

            switch (opCode)
            {
                case 0: // SKIP / STOP
                    {
                        if (format == 0)
                        {
                            return STOP; // Stop execution
                        }
                        else if (format == 2) // Print
                        {
                            printTrace.Append(regs[regi]).Append("\r\n");
                            Console.WriteLine(regs[regi].ToString() + "\r\n");
                            return OK;
                        } 
                        else 
                        {
                            return OK; // Do nothing
                        }
                    }
                case 1: // LD
                    {
                        if (regs[regi] < 0 || regs[regi] > memory.Length)
                            return MEMORY_OUT_OF_BOUND;

                        regs[regj] = LoadLWord(regs[regi]);
                        return OK;
                    }
                case 2: // LDC / LDA
                    {
                        if (format == 0)
                        {
                            if (Program.showTrace)
                                executionTrace.Append("  const= ").Append(LoadLWord(regs[PC])).Append("\n");

                            regs[regj] = regs[regi] + LoadLWord(regs[PC]);
                            regs[PC] += 4;
                            
                            // ATTENTION: I am not sure about that
                            /*if (regs[PC] - 1 % 4 == 0)
                            {
                                regs[regj] = regs[regi] + LoadLWord(regs[PC] + 2);
                                regs[PC] += 6; // Is it okay? We'll see...
                            }
                            else
                            {
                                regs[regj] = regs[regi] + LoadLWord(regs[PC]);
                                regs[PC] += 4;
                            }*/
                        }
                        else
                        {
                            regs[regj] = Convert.ToUInt32(regi);
                        }

                        return OK;
                    }
                case 3: // ST
                    {
                        if (regs[regj] < 0 || regs[regj] > memory.Length)
                            return MEMORY_OUT_OF_BOUND;

                        StoreLWord(regs[regj], regs[regi]);
                        return OK;
                    }
                case 4: // MOV
                    {
                        if (format == 3)
                        {
                            regs[regj] = regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] &= 0xffff0000;
                            regs[regj] |= 0x0000ffff & regs[regi];
                        }
                        else
                        {
                            regs[regj] &= 0xffffff00;
                            regs[regj] |= 0x000000ff & regs[regi];
                        }
                        return OK;
                    }
                case 5: // ADD
                    {
                        if (format == 3)
                        {
                            regs[regj] += regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | ((0x0000ffff & regs[regj]) + (0x0000ffff & regs[regi]));
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | ((0x000000ff & regs[regj]) + (0x000000ff & regs[regi]));
                        }                        
                        return OK;
                    }
                case 6: // SUB
                    {
                        if (format == 3)
                        {
                            regs[regj] -= regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | ((0x0000ffff & regs[regj]) - (0x0000ffff & regs[regi]));
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | ((0x000000ff & regs[regj]) - (0x000000ff & regs[regi]));
                        }
                        return OK;
                    }
                case 7: // ASR
                    {
                        if (format == 3)
                        {
                            regs[regj] = ((regs[regi] >> 1) & (0xbfffffff)) | (0x80000000 & regs[regi]);
                        }
                        else if (format == 1)
                        {
                            regs[regj] = ((regs[regi] >> 1) & (0x0000bfff)) | (0x00008000 & regs[regi]) | (0xffff0000 & regs[regi]);
                        }
                        else
                        {
                            regs[regj] = ((regs[regi] >> 1) & (0x000000bf)) | (0x00000080 & regs[regi]) | (0xffffff00 & regs[regi]);
                        }
                        return OK;
                    }
                case 8: // ASL
                    {
                        if (format == 3)
                        {
                            regs[regj] = ((regs[regi] << 1) & (0x7fffffff)) | (0x80000000 & regs[regi]);
                        }
                        else if (format == 1)
                        {
                            regs[regj] = ((regs[regi] << 1) & (0x00007fff)) | (0x00008000 & regs[regi]) | (0xffff0000 & regs[regi]);
                        }
                        else
                        {
                            regs[regj] = ((regs[regi] << 1) & (0x0000007f)) | (0x00000080 & regs[regi]) | (0xffffff00 & regs[regi]);
                        }
                        return OK;
                    }
                case 9: // OR
                    {
                        if (format == 3)
                        {
                            regs[regj] |= regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | (0x0000ffff & regs[regj]) | (0x0000ffff & regs[regi]);
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | (0x000000ff & regs[regj]) | (0x000000ff & regs[regi]);
                        }
                        return OK;
                    }
                case 10: // AND
                    {
                        if (format == 3)
                        {
                            regs[regj] &= regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | (0x0000ffff & regs[regj] & regs[regi]);
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | (0x000000ff & regs[regj] & regs[regi]);
                        }
                        return OK;
                    }
                case 11: // XOR
                    {
                        if (format == 3)
                        {
                            regs[regj] ^= regs[regi];
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | ((0x0000ffff & regs[regj]) ^ (0x0000ffff & regs[regi]));
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | ((0x000000ff & regs[regj]) ^ (0x000000ff & regs[regi]));
                        }
                        return OK;
                    }
                case 12: // LSL
                    {
                        if (format == 3)
                        {
                            regs[regj] = regs[regi] << 1;
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | (0x0000ffff & (regs[regi] << 1));
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | (0x000000ff & (regs[regi] << 1));
                        }
                        return OK;
                    }
                case 13: // LSR
                    {
                        if (format == 3)
                        {
                            regs[regj] = regs[regi] >> 1;
                        }
                        else if (format == 1)
                        {
                            regs[regj] = (0xffff0000 & regs[regj]) | (0x00007fff & (regs[regi] >> 1));
                        }
                        else
                        {
                            regs[regj] = (0xffffff00 & regs[regj]) | (0x0000007f & (regs[regi] >> 1));
                        }
                        return OK;
                    }
                case 14: // CND
                    {
                        if (format == 3)
                        {
                            int ri = ConvertFromTwosComplement(regs[regi]);
                            int rj = ConvertFromTwosComplement(regs[regj]);
                            regs[regj] &= 0xfffffff0;
                            if (ri > rj) regs[regj] |= 1;
                            if (ri < rj) regs[regj] |= 2;
                            if (ri == rj) regs[regj] |= 4;
                        }
                        else if (format == 1)
                        {
                            int ri = ConvertFromTwosComplement(regs[regi] & 0x0000ffff);
                            int rj = ConvertFromTwosComplement(regs[regj] & 0x0000ffff);
                            regs[regj] &= 0xfffffff0;
                            if (ri > rj) regs[regj] |= 1;
                            if (ri < rj) regs[regj] |= 2;
                            if (ri == rj) regs[regj] |= 4;
                        }
                        else
                        {
                            int ri = ConvertFromTwosComplement(regs[regi] & 0x000000ff);
                            int rj = ConvertFromTwosComplement(regs[regj] & 0x000000ff);
                            regs[regj] &= 0xfffffff0;
                            if (ri > rj) regs[regj] |= 1;
                            if (ri < rj) regs[regj] |= 2;
                            if (ri == rj) regs[regj] |= 4;
                        }
                        return OK;
                    }
                case 15: // CBR
                    {
                        int ri = ConvertFromTwosComplement(regs[regi]);
                        if (ri != 0)
                        {
                            uint temp = regs[PC];
                            regs[PC] = regs[regj];
                            regs[regi] = temp; // ATTENTION: Should be +2 by design. However, I think it is not right since we skip one command.
                        }
                        if (Program.showTrace)
                        {
                            executionTrace.Append("\r\n");
                        }
                        return OK;
                    }
                default:
                    break;
            }

            return STOP;
        }

        private void StoreLWord(uint address, uint lword)
        {
            byte[] bytes = BitConverter.GetBytes(lword);
            memory[address + 0] = bytes[3];
            memory[address + 1] = bytes[2];
            memory[address + 2] = bytes[1];
            memory[address + 3] = bytes[0];
        }

        private uint LoadLWord(uint address)
        {
            return BitConverter.ToUInt32(
                new byte[] {
                    memory[address + 3],
                    memory[address + 2],
                    memory[address + 1],
                    memory[address + 0]
                }
                );
        }

        private uint LoadLWordFromBinary(uint address)
        {
            return BitConverter.ToUInt32(
                new byte[] {
                    binary[address + 3],
                    binary[address + 2],
                    binary[address + 1],
                    binary[address + 0]
                }
                );
        }

        private int ConvertFromTwosComplement(uint constant)
        {
            if ((constant & 0x80000000) > 0)
            {
                return -(int)~(constant - 1);
            }
            else
            {
                return (int)constant;
            }
        }
    }
}
