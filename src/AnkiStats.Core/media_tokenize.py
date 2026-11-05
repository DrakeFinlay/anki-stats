"""Local stdin -> aggregated Sudachi morphemes. No network or source-file access."""
import collections
import importlib.metadata
import json
import re
import sys

from sudachipy import dictionary, tokenizer

analyzer = dictionary.Dictionary(dict="core").create()
counts = collections.Counter()
examples = {}
for line in sys.stdin:
    # Bound individual tokenizer input while preserving normal lines and sentences.
    for sentence in re.findall(r'[^。！？!?]+[。！？!?]*[」』”"]?', line):
        sentence = sentence.strip()
        for start in range(0, len(sentence), 8000):
            for word in analyzer.tokenize(sentence[start:start + 8000], tokenizer.Tokenizer.SplitMode.C):
                pos = word.part_of_speech()
                if pos[0] in ("補助記号", "記号", "空白"):
                    continue
                key = (word.surface(), word.dictionary_form(), word.normalized_form(), word.reading_form(), "/".join(pos))
                counts[key] += 1
                if len(sentence) <= 500:
                    examples.setdefault(key, sentence)
json.dump({"analyzer": "SudachiPy " + importlib.metadata.version("SudachiPy")
           + "; SudachiDict-core " + importlib.metadata.version("SudachiDict-core") + "; split C; helper v2; source sentences",
           "tokens": [dict(zip(("surface", "lemma", "normalized", "reading", "pos", "count", "sentence"), key + (count, examples.get(key, ""))))
                      for key, count in counts.items()]}, sys.stdout, ensure_ascii=False)
