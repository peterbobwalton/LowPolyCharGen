"""Run inside an open LowPolyCharTest editor (Output Log, Python):

    py "C:/source/Claude/LowPolyCharGen/tools/lpct_refresh.py"

Imports every character exported to LowPolyCharGen/out/characters onto the Toon Soldiers skeleton
and rebuilds the showcase lineup in Lvl_ThirdPerson (saved). Nothing else is touched.
"""
import os, sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import import_unreal

import_unreal.main([os.path.join(TOOLS, "..", "out", "characters"), "/Game/LowPolyCharGen/Characters"])
LPCG_INTERACTIVE = True
exec(open(os.path.join(TOOLS, "unreal_lineup.py")).read())
