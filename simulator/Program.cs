using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MiningSim_Detailed_TimeStep
{
    // ==========================================================================================
    // CONFIGURATION & CONSTANTS
    // ==========================================================================================
    public static class Config
    {
        // USER SETTINGS
        public const int TOTAL_MONTHS_TO_SIMULATE = 1200; // More months = better "2-Sigma" accuracy
        public static readonly List<int> PAYOUT_LIST_SIZES = new List<int> { 16, 50, 100, 300, 512 };

        // ECONOMIC CONSTANTS
        public const double BTC_PRICE_USD = 100000.0;
        public const double BLOCK_REWARD_BTC = 3.125;
        public const double TRANSACTION_FEES_BTC = 0.05; // Goes to block finder only

        // NETWORK CONSTANTS (SCALED)
        // Current Difficulty approx 101 Trillion
        public const double REAL_NETWORK_DIFFICULTY = 149301205959699.9; 
        
        // We scale so 1 Unit of Work = 1 TH (1 Trillion Hashes)
        // The "Scaled Difficulty" is the difficulty value a 1TH share must beat to solve a block.
        // Formula: (RealDiff * 2^32) / 10^12
        // Derived: ~ 101.6T * 4.29m ~ 436 Billion
        public const double SCALED_NETWORK_DIFFICULTY = (REAL_NETWORK_DIFFICULTY * 4294967296.0) / 1_000_000_000_000.0;
        
        // TIME CONSTANTS
        public const int SECONDS_PER_MONTH = 2629743; // 30.44 days
    }

    public class Program
    {
        // Global Progress Tracker
        static long _totalSecondsSimulated = 0;
        static long _totalSecondsTarget = (long)Config.TOTAL_MONTHS_TO_SIMULATE * Config.SECONDS_PER_MONTH;
        static object _consoleLock = new object();

        public static void Main(string[] args)
        {
            Console.WriteLine("--- SECOND-BY-SECOND POOL MECHANICS SIMULATION ---");
            Console.WriteLine($"Simulating {Config.TOTAL_MONTHS_TO_SIMULATE} months per configuration.");
            Console.WriteLine($"Network Diff (Scaled): {Config.SCALED_NETWORK_DIFFICULTY:N0}");
            Console.WriteLine($"Block Reward: {Config.BLOCK_REWARD_BTC} BTC + {Config.TRANSACTION_FEES_BTC} Fees");
            Console.WriteLine("--------------------------------------------------\n");

            // 1. Define Miners
            var miners = new List<Miner>
            {
                new Miner { Name = "MegaMi(5.92 EH)",  Hashrate = 5000000.0 }, 
                new Miner { Name = "BigPol (1.75 EH)",  Hashrate = 1750000.0 },
                new Miner { Name = "BigPol (1.15 EH)",  Hashrate = 1150000.0 },
                new Miner { Name = "BigPol (1.09 EH)",  Hashrate = 1090000.0 },
                new Miner { Name = "BigPol (1.02 EH)",  Hashrate = 1020000.0 },
                new Miner { Name = "BigPol (618 PH)",  Hashrate = 618000.0 },
                new Miner { Name = "BigPol (600 PH)",  Hashrate = 600000.0 },
                new Miner { Name = "BigPol (546 PH)",  Hashrate = 546000.0 },
                new Miner { Name = "BigPol (519 PH)",  Hashrate = 519000.0 },
                new Miner { Name = "BigPol (456 PH)",  Hashrate = 456000.0 },
                new Miner { Name = "BigPol (428 PH)",  Hashrate = 428000.0 },
                new Miner { Name = "BigPol (286 PH)",  Hashrate = 286000.0 },
                new Miner { Name = "BigPol (214 PH)",  Hashrate = 215000.0 },
                new Miner { Name = "BigPol (199 PH)",  Hashrate = 199000.0 },
                new Miner { Name = "BigPol (110 PH)",  Hashrate = 110000.0 },
                new Miner { Name = "MidPool (10 PH)",  Hashrate = 10000.0 },   
                new Miner { Name = "SmallFarm (1 PH)", Hashrate = 1000.0 },
                new Miner { Name = "Bitaxe (1 TH)",    Hashrate = 1.0 }        

            };

            // Calculate Pool Hashrate and Network Context
            double poolHashrateTH = miners.Sum(m => m.Hashrate);
            double networkHashrateTH = (Config.SCALED_NETWORK_DIFFICULTY * 1_000_000_000_000.0) / 600.0 / 4294967296.0 * 1000.0; 
            // Simplified Network Hashrate estimate: Diff * 2^32 / 600. Divide by 10^12 for TH.
            double estNetHashrateTH = (Config.REAL_NETWORK_DIFFICULTY * 4294967296.0) / 600.0 / 1e12;

            Console.WriteLine($"Pool Hashrate: {poolHashrateTH:N0} TH/s");
            Console.WriteLine($"Est. Network Hashrate: {estNetHashrateTH:N0} TH/s");
            Console.WriteLine($"Pool Market Share: {(poolHashrateTH/estNetHashrateTH)*100:F4}%\n");

            // 2. Run Solo Lotto Stats (Static Math)
            PrintSoloLottoStats(miners, estNetHashrateTH);

            // 3. Run Simulation for each Payout List Size
            foreach (int N in Config.PAYOUT_LIST_SIZES)
            {
                // Reset Progress
                _totalSecondsSimulated = 0;
                Console.WriteLine($"\n\n>>> SIMULATING PAYOUT LIST SIZE: {N} <<<");
                
                // Run Parallel Simulation
                var results = RunParallelSimulation(miners, N);
                
                // Print Results
                PrintPoolStats(results, N);
            }
        }

        static List<MinerResult> RunParallelSimulation(List<Miner> minerDefs, int maxListSize)
        {
            // Prepare a container for results (Thread-safe collection not strictly needed if we aggregate at end)
            var allMonthlyResults = new System.Collections.Concurrent.ConcurrentBag<List<MonthlyStat>>();
            
            // Start Progress Bar Thread
            var cts = new CancellationTokenSource();
            var progressTask = Task.Run(() => DrawProgressBar(cts.Token));

            // PARALLEL LOOP: 1 Iteration = 1 Month
            Parallel.For(0, Config.TOTAL_MONTHS_TO_SIMULATE, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, monthIndex =>
            {
                // Each thread gets its own Random and State
                // We use a cryptographically strong seed just in case, though tick count is usually fine
                var rand = new Random(Guid.NewGuid().GetHashCode());
                
                // Initialize Local State
                var onDeckList = new List<Share>(); // The buffer
                var winnersList = new List<Share>(); // The previous winners
                bool winnersListInitialized = false;

                // Local stats for this month
                var monthlyStats = new Dictionary<string, double>();
                foreach (var m in minerDefs) monthlyStats[m.Name] = 0;

                // --- SECOND-BY-SECOND LOOP ---
                for (int sec = 0; sec < Config.SECONDS_PER_MONTH; sec++)
                {
                    Share blockFinderShare = null;
                    double bestBlockDiff = 0;

                    // A. MINING PHASE (Generate Shares)
                    // We generate ONE "Best Share" per miner using Inverse Transform Sampling
                    foreach (var miner in minerDefs)
                    {
                        // Optimization: Max of N Pareto samples. 
                        // x = 1 / (1 - u^(1/N))
                        // We assume base difficulty is 1.0 (1 TH unit).
                        double u = rand.NextDouble();
                        
                        // Avoid division by zero or log(0) issues
                        if (u >= 1.0) u = 0.999999999;
                        
                        double exponent = 1.0 / miner.Hashrate;
                        double uPow = Math.Pow(u, exponent);
                        double bestDiff = 1.0 / (1.0 - uPow);

                        // 1. Check if Block Found (Beat Network)
                        if (bestDiff > Config.SCALED_NETWORK_DIFFICULTY)
                        {
                            // BLOCK FOUND!
                            // If multiple miners find a block in the same second (rare), highest diff wins
                            if (bestDiff > bestBlockDiff)
                            {
                                bestBlockDiff = bestDiff;
                                blockFinderShare = new Share 
                                { 
                                    MinerName = miner.Name, 
                                    Difficulty = bestDiff, 
                                    IsBlockFinder = true 
                                };
                            }
                        }

                        // 2. Add to OnDeckList (Limit memory, keep Top N)
                        // Optimization: Only add if list isn't full OR if diff is better than the worst in list
                        // (To save sorting time on every single share)
                        // However, since 0-diff shares exist, we almost always add/replace.
                        
                        // We'll process list updates in bulk or just add and sort later?
                        // For fidelity, we should insert carefully.
                        // Let's optimize: Check if better than lowest *active* score.
                        if (onDeckList.Count < maxListSize || bestDiff > (onDeckList.Count > 0 ? onDeckList[^1].Difficulty : 0))
                        {
                            onDeckList.Add(new Share { MinerName = miner.Name, Difficulty = bestDiff });
                            
                            // Keep sorted and trimmed
                            // Note: For 300 items, sorting every insert is okay-ish, but batching is better.
                            // Given 4 miners, we sort 4 times per second. Trivial.
                            onDeckList.Sort((a, b) => b.Difficulty.CompareTo(a.Difficulty)); // Descending
                            if (onDeckList.Count > maxListSize)
                            {
                                onDeckList.RemoveRange(maxListSize, onDeckList.Count - maxListSize);
                            }
                        }
                    }

                    // B. BLOCK FOUND LOGIC
                    if (blockFinderShare != null)
                    {
                        // 0. Recursive Initialization
                        if (!winnersListInitialized)
                        {
                            winnersList = new List<Share>(onDeckList);
                            winnersListInitialized = true;
                        }

                        // 1. Add Finder to Winners (for immediate payout)
                        // The user said: "There is a list called Winners List... PLUS one share for the miner who just found"
                        // We add them temporarily for calculation
                        winnersList.Add(blockFinderShare);

                        // 2. Process Payouts
                        double splitReward = Config.BLOCK_REWARD_BTC / winnersList.Count;
                        
                        foreach (var winner in winnersList)
                        {
                            monthlyStats[winner.MinerName] += splitReward;
                            
                            // Transaction Fees (Bonus to finder only)
                            if (winner.IsBlockFinder)
                            {
                                monthlyStats[winner.MinerName] += Config.TRANSACTION_FEES_BTC;
                            }
                        }

                        // 3. State Rotation
                        // "On Deck List gets copied into Winners List"
                        winnersList = new List<Share>(onDeckList);
                        
                        // "Keep On Deck List intact, but reset all difficulty slots to zero (except block winner)"
                        foreach (var share in onDeckList)
                        {
                            share.Difficulty = 0; 
                            // Note: They stay in the list (occupying spots), but have 0 score.
                            // They will be pushed out easily by any share > 0.
                        }

                        // Ensure the block finder is On Deck with their high score (Guaranteed spot)
                        // Check if finder is already in list (likely yes, they found the block).
                        // If their share object is there, it was zeroed. We must restore/add it.
                        // Actually, we generated 'blockFinderShare' separately. Let's force add it.
                        onDeckList.Add(blockFinderShare);
                        onDeckList.Sort((a, b) => b.Difficulty.CompareTo(a.Difficulty));
                        if (onDeckList.Count > maxListSize) onDeckList.RemoveRange(maxListSize, onDeckList.Count - maxListSize);
                    }

                    // Progress Update (Interlocked is fast)
                    Interlocked.Increment(ref _totalSecondsSimulated);
                }

                // End of Month: Package results
                var monthResult = new List<MonthlyStat>();
                foreach(var kvp in monthlyStats)
                {
                    monthResult.Add(new MonthlyStat { MinerName = kvp.Key, TotalBtc = kvp.Value });
                }
                allMonthlyResults.Add(monthResult);
            });

            cts.Cancel();
            try { progressTask.Wait(); } catch { }

            // AGGREGATE RESULTS
            var finalResults = new List<MinerResult>();
            foreach(var def in minerDefs)
            {
                var minerData = new MinerResult { Name = def.Name, HashrateTH = def.Hashrate };
                
                // Flatten all months for this miner
                var minerMonths = allMonthlyResults
                    .SelectMany(x => x)
                    .Where(x => x.MinerName == def.Name)
                    .Select(x => x.TotalBtc)
                    .ToList();

                minerData.MonthlyIncomesBTC = minerMonths;
                finalResults.Add(minerData);
            }
            return finalResults;
        }

        // ==========================================================================================
        // HELPERS & CLASSES
        // ==========================================================================================

        static void DrawProgressBar(CancellationToken token)
        {
            Console.WriteLine(); // Spacing
            while (!token.IsCancellationRequested)
            {
                double progress = (double)Interlocked.Read(ref _totalSecondsSimulated) / _totalSecondsTarget;
                int width = 50;
                int filled = (int)(progress * width);
                string bar = new string('#', filled) + new string('-', width - filled);
                
                lock(_consoleLock)
                {
                    Console.Write($"\rProgress: [{bar}] {progress*100:F1}%   ");
                }
                Thread.Sleep(500);
            }
            Console.WriteLine();
        }

        static void PrintSoloLottoStats(List<Miner> miners, double netHashrateTH)
        {
            Console.WriteLine("=== SOLO LOTTO STATS (No Pool) ===");
            Console.WriteLine($"Payout Target: {Config.BLOCK_REWARD_BTC + Config.TRANSACTION_FEES_BTC} BTC (${(Config.BLOCK_REWARD_BTC + Config.TRANSACTION_FEES_BTC) * Config.BTC_PRICE_USD:N0})");
            Console.WriteLine($"{"Miner",-20} | {"Expected Blocks/Month",-22} | {"Avg Time to Block",-20}");
            Console.WriteLine(new string('-', 60));

            const double AVERAGE_BLOCK_TIME_SECONDS = 600.0;
            const double DAYS_PER_YEAR = 365.25;

            foreach (var m in miners)
            {
                // 1. Calculate Expected Time to Find a Block
                // Expected Time = (Net Hashrate / Miner Hashrate) * Block Time
                double ratio = netHashrateTH / m.Hashrate;
                double avgSeconds = ratio * AVERAGE_BLOCK_TIME_SECONDS;
                
                // 2. Expected Blocks per Month (Lambda)
                double lambdaMonth = Config.SECONDS_PER_MONTH / avgSeconds;
                
                // 3. Format Chance as "1/X"
                string chancePerMonthStr;
                if (lambdaMonth == 0)
                {
                    chancePerMonthStr = "1/inf"; // Essentially never
                }
                else if (lambdaMonth >= 1.0)
                {
                    // For common events (e.g., 4 blocks/month), display the actual number
                    chancePerMonthStr = $"{lambdaMonth:F2} blocks"; 
                }
                else
                {
                    // For rare events (e.g., 0.0004 blocks/month), display 1/X
                    double inverseLambda = 1.0 / lambdaMonth;
                    
                    // Use k for thousands for better readability in the table
                    if (inverseLambda >= 1000)
                    {
                        chancePerMonthStr = $"1/{inverseLambda/1000.0:F0}k months";
                    }
                    else
                    {
                        chancePerMonthStr = $"1/{inverseLambda:F0} months";
                    }
                }

                // 4. Time Conversion (for Avg Time to Block column)
                const double SECONDS_PER_DAY = 86400.0;
                double avgYears = avgSeconds / SECONDS_PER_DAY / DAYS_PER_YEAR;

                // Display formatting for years/days
                string timeStr = avgYears > 10000 ? $"{avgYears/1000:F0}k years" :
                                avgYears > 1000 ? $"{avgYears/1000:F1}k years" :
                                avgYears < 1 ? $"{avgYears*DAYS_PER_YEAR:F1} days" : 
                                $"{avgYears:F1} years";

                Console.WriteLine($"{m.Name,-20} | {chancePerMonthStr,-22} | {timeStr,-20}");
            }
            Console.WriteLine();
        }

        static void PrintPoolStats(List<MinerResult> results, int N)
        {
            Console.WriteLine($"\nRESULTS FOR PAYOUT LIST SIZE: {N} (Equal Split: {(Config.BLOCK_REWARD_BTC / N):F5} BTC/share)");
            Console.WriteLine($"{"Miner",-16} | {"Avg Monthly",-14} | {"Min Monthly",-12} | {"Max Monthly",-12} | {"2-Sigma Low",-12} | {"CoV",-6} | {"Lotto(Chance)",-14} | {"Lotto(Time)",-12}");
            Console.WriteLine(new string('-', 115));

            foreach (var r in results)
            {
                var incomesUSD = r.MonthlyIncomesBTC.Select(b => b * Config.BTC_PRICE_USD).ToList();
                
                double meanUSD = incomesUSD.Average();
                double minUSD = incomesUSD.Min();
                double maxUSD = incomesUSD.Max();
                
                // StdDev
                double sumSq = incomesUSD.Sum(val => (val - meanUSD) * (val - meanUSD));
                double stdDev = Math.Sqrt(sumSq / incomesUSD.Count);
                
                // 2-Sigma (95% Confidence Lower Bound)
                double twoSigma = meanUSD - (2 * stdDev);
                if (twoSigma < 0) twoSigma = 0;

                double cov = meanUSD > 0 ? stdDev / meanUSD : 0;

                // POOL LOTTO STATS
                // Chance of ANY payout = Months with > 0 income / Total Months
                int monthsWithPay = r.MonthlyIncomesBTC.Count(x => x > 0);
                double chanceAnyPay = (double)monthsWithPay / Config.TOTAL_MONTHS_TO_SIMULATE;
                
                string timeToPayStr;
                if (chanceAnyPay <= 0) timeToPayStr = "Never";
                else 
                {
                    double avgMonthsToPay = 1.0 / chanceAnyPay;
                    if (avgMonthsToPay < 1.0) timeToPayStr = "< 1 Month";
                    else timeToPayStr = $"{avgMonthsToPay:F1} Months";
                }

                Console.WriteLine($"{r.Name,-16} | ${meanUSD,14:N0} | ${minUSD,12:N0} | ${maxUSD,12:N0} | ${twoSigma,12:N0} | {cov,6:F2} | {chanceAnyPay*100,14:F1}%    | {timeToPayStr,-12}");
            }
        }
    }

    // Data Structures
    public class Miner { public string Name; public double Hashrate; }
    
    public class Share 
    { 
        public string MinerName; 
        public double Difficulty; 
        public bool IsBlockFinder;
    }

    public class MonthlyStat { public string MinerName; public double TotalBtc; }

    public class MinerResult 
    { 
        public string Name; 
        public double HashrateTH; 
        public List<double> MonthlyIncomesBTC; 
    }
}