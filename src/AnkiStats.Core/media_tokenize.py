"""Local stdin -> aggregated Sudachi morphemes. No network or source-file access."""
import collections
import importlib.metadata
import json
import sys

from sudachipy import dictionary, tokenizer

analyzer = dictionary.Dictionary(dict="core").create()
counts = collections.Counter()
for line in sys.stdin:
    # Bound individual tokenizer input while preserving normal lines and sentences.
    for start in range(0, len(line), 8000):
        for word in analyzer.tokenize(line[start:start + 8000], tokenizer.Tokenizer.SplitMode.C):
            pos = word.part_of_speech()
            if pos[0] in ("補助記号", "記号", "空白"):
                continue
            counts[(word.surface(), word.dictionary_form(), word.normalized_form(),
                    word.reading_form(), "/".join(pos))] += 1
json.dump({"analyzer": "SudachiPy " + importlib.metadata.version("SudachiPy")
           + "; SudachiDict-core " + importlib.metadata.version("SudachiDict-core") + "; split C; helper v1",
           "tokens": [dict(zip(("surface", "lemma", "normalized", "reading", "pos", "count"), key + (count,)))
                      for key, count in counts.items()]}, sys.stdout, ensure_ascii=False)
