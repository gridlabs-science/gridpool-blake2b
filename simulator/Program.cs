// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiningSim
{
    class Program
    {
        // ================= CONFIGURATION =================
        const int TOTAL_BLOCKS = 10000;         // How long to run the test
        const double BLOCK_REWARD = 3.125;      // BTC
        
        // YOUR CONFIG: Size of the payout list (16 to 300)
        const int TOP_N_PAYOUT_SIZE = 16;      
        
        // POOL SETTINGS: How many valid shares does the pool process per block?
        // (This represents the "resolution" of the simulation. 
        //  Real pools process thousands, 5000 is enough for statistical significance here).
        const int SHARES_PER_BLOCK = 5000;      
        // =================================================

        static void Main(string[] args)
        {
            Console.WriteLine($"--- MINING PAYOUT SIMULATION ({TOP_N_PAYOUT_SIZE} Top Shares) ---");
            Console.WriteLine($"Simulating {TOTAL_BLOCKS:N0} blocks with {SHARES_PER_BLOCK:N0} shares per block.\n");

            // 1. Setup Miners (Whale, Mid, Small, Micro)
            var miners = new List<Miner>
            {
                new Miner { Name = "Whale  ", Hashrate = 50000 }, // 50% of pool
                new Miner { Name = "MidTier", Hashrate = 25000 }, // 25% of pool
                new Miner { Name = "Small  ", Hashrate = 20000 }, // 20% of pool
                new Miner { Name = "Micro  ", Hashrate = 4990  }, // 4.999% of pool
                new Miner { Name = "Bitaxe ", Hashrate = 10    }  // 0.010% of pool
            };

            double totalPoolHashrate = miners.Sum(m => m.Hashrate);
            
            // Calculate "Fair" Expected Probability for each miner
            foreach (var m in miners) 
            {
                m.Probability = m.Hashrate / totalPoolHashrate;
            }

            Random rand = new Random();

            // 2. Main Simulation Loop
            for (int block = 0; block < TOTAL_BLOCKS; block++)
            {
                // A. Generate Shares for this round
                var roundShares = new List<Share>(SHARES_PER_BLOCK);

                for (int i = 0; i < SHARES_PER_BLOCK; i++)
                {
                    // Pick which miner found this share (Weighted Random)
                    var minerIndex = GetWeightedMinerIndex(miners, rand, totalPoolHashrate);
                    
                    // Generate Difficulty (Pareto Distribution)
                    // Logic: Hash is Uniform(0,1). Difficulty is 1/Hash.
                    // We use 1.0 - rand.NextDouble() to avoid DivideByZero
                    double hashVal = 1.0 - rand.NextDouble(); 
                    double difficulty = 1.0 / hashVal;

                    roundShares.Add(new Share { MinerIndex = minerIndex, Difficulty = difficulty });
                }

                // --- STRATEGY 1: STANDARD PPLNS (Control Group) ---
                // In pure PPLNS, you get paid based on your % of total difficulty submitted
                double totalRoundDifficulty = roundShares.Sum(s => s.Difficulty);
                foreach (var share in roundShares)
                {
                    double shareReward = (share.Difficulty / totalRoundDifficulty) * BLOCK_REWARD;
                    miners[share.MinerIndex].Rewards_PPLNS += shareReward;
                    
                    // Track variance: (Actual Reward - Expected Mean)^2
                    // Note: This is a simplified variance tracking for the block total, calculated later
                }

                // --- STRATEGY 2: TOP N EQUAL SPLIT (Your Method) ---
                // 1. Sort by Difficulty Descending
                // 2. Take Top N
                // 3. Split Reward Equally
                var topShares = roundShares
                    .OrderByDescending(s => s.Difficulty)
                    .Take(TOP_N_PAYOUT_SIZE)
                    .ToList();

                double rewardPerWinner = BLOCK_REWARD / topShares.Count;

                // Track who won this block for variance calc
                var blockRewardsTopN = new double[miners.Count];

                foreach (var share in topShares)
                {
                    miners[share.MinerIndex].Rewards_TopN += rewardPerWinner;
                    blockRewardsTopN[share.MinerIndex] += rewardPerWinner;
                }

                // Calculate Variance for this block (Top N Method)
                for(int m=0; m<miners.Count; m++)
                {
                    double expectedBlockReward = miners[m].Probability * BLOCK_REWARD;
                    double actualBlockReward = blockRewardsTopN[m];
                    double diff = actualBlockReward - expectedBlockReward;
                    miners[m].VarianceSum_TopN += (diff * diff);
                }
            }

            // 3. Report Results
            Console.WriteLine("Miners      | Exp. % | PPLNS % | TopN % | TopN StdDev (Risk) | Fairness Check");
            Console.WriteLine("-----------------------------------------------------------------------------");

            foreach (var m in miners)
            {
                double totalDistributedPPLNS = miners.Sum(x => x.Rewards_PPLNS);
                double totalDistributedTopN = miners.Sum(x => x.Rewards_TopN);

                double pplnsShare = (m.Rewards_PPLNS / totalDistributedPPLNS) * 100.0;
                double topNShare = (m.Rewards_TopN / totalDistributedTopN) * 100.0;
                
                // Standard Deviation per block (Volatility of payout)
                double stdDev = Math.Sqrt(m.VarianceSum_TopN / TOTAL_BLOCKS);

                // Fairness: Ratio of Actual/Expected. 1.0 is perfect.
                double fairness = topNShare / (m.Probability * 100.0);

                Console.WriteLine($"{m.Name}     | {m.Probability*100,5:F1}% | {pplnsShare,6:F2}% | {topNShare,5:F2}% | {stdDev,14:F4}     | {fairness,8:F4} x");
            }
            
            Console.WriteLine("\nKEY:");
            Console.WriteLine("- TopN %: The actual percentage of rewards captured.");
            Console.WriteLine("- StdDev: Volatility. Higher = Spikier payouts (bad for small miners).");
            Console.WriteLine("- Fairness: Should be close to 1.000. If 0.9 or 1.1, the system is biased.");
            
            Console.ReadLine();
        }

        // Helper to pick a miner based on hashrate weight
        static int GetWeightedMinerIndex(List<Miner> miners, Random rand, double totalHashrate)
        {
            double roll = rand.NextDouble() * totalHashrate;
            double sum = 0;
            for (int i = 0; i < miners.Count; i++)
            {
                sum += miners[i].Hashrate;
                if (roll < sum) return i;
            }
            return miners.Count - 1;
        }
    }

    class Miner
    {
        public string Name { get; set; }
        public double Hashrate { get; set; }
        public double Probability { get; set; } // Expected share
        
        public double Rewards_PPLNS { get; set; }
        public double Rewards_TopN { get; set; }
        
        // Sum of squared differences for variance calculation
        public double VarianceSum_TopN { get; set; } 
    }

    class Share
    {
        public int MinerIndex { get; set; }
        public double Difficulty { get; set; }
    }
}