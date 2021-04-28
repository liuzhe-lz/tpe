'''
Copyright 2020 The Ray Authors.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

This source file is adapted here because ray does not fully support Windows.
'''
import copy
import glob
import logging
import os
import time
from typing import Dict, Optional, Union, List, Tuple

logger = logging.getLogger(__name__)

UNRESOLVED_SEARCH_SPACE = str(
    "You passed a `{par}` parameter to {cls} that contained unresolved search "
    "space definitions. {cls} should however be instantiated with fully "
    "configured search spaces only. To use Ray Tune's automatic search space "
    "conversion, pass the space definition as part of the `config` argument "
    "to `tune.run()` instead.")

UNDEFINED_SEARCH_SPACE = str(
    "Trying to sample a configuration from {cls}, but no search "
    "space has been defined. Either pass the `{space}` argument when "
    "instantiating the search algorithm, or pass a `config` to "
    "`tune.run()`.")

UNDEFINED_METRIC_MODE = str(
    "Trying to sample a configuration from {cls}, but the `metric` "
    "({metric}) or `mode` ({mode}) parameters have not been set. "
    "Either pass these arguments when instantiating the search algorithm, "
    "or pass them to `tune.run()`.")


_logged = set()
_disabled = False
_periodic_log = False
_last_logged = 0.0


class Searcher:
    FINISHED = "FINISHED"
    CKPT_FILE_TMPL = "searcher-state-{}.pkl"

    @property
    def metric(self) -> str:
        """The training result objective value attribute."""
        return self._metric

    @property
    def mode(self) -> str:
        """Specifies if minimizing or maximizing the metric."""
        return self._mode

# (Optional) Default (anonymous) metric when using tune.report(x)
DEFAULT_METRIC = "_metric"

# (Auto-filled) The index of this training iteration.
TRAINING_ITERATION = "training_iteration"
