import numpy as np
from flow2 import FLOW2
from blendsearch import CFO
from sample import (
    choice,
    lograndint,
    loguniform,
    randint,
    uniform,
)

np.random.seed(0)

init_config = {
    'batch': 32,
    'conv': 3,
    'dropout': 0.5,
    'lr': 0.001,
    'hidden': 128,
}
space = {
    'batch': randint(16, 32),
    'conv': choice([2, 3, 5, 7]),
    'dropout': uniform(0.5, 0.9),
    'lr': loguniform(0.0001, 0.1),
    'hidden': lograndint(32, 1024),
}

searcher = CFO(
    low_cost_partial_config = init_config,
    metric = 'loss',
    space = space,
    #random = np.random.RandomState(20),
)

configs = []

for i in range(100):
    configs.append(searcher.suggest(str(i)))
    result = {
        'time_total_s': np.random.random() * 60,
        'loss': np.random.random(),
    }
    for k, v in configs[-1].items():
        result['config/' + k] = v
    searcher.on_trial_complete(str(i), result)

for i in range(10):
    print(configs[i * 10 + 9])

#assert configs[9] == {'batch': 32, 'conv': 3, 'dropout': 0.5803017700282026, 'lr': 0.00029272264078005763, 'hidden': 136}
#print('OK')
