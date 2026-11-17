#!/usr/bin/env python3
"""Falha o build se a cobertura de linhas do relatório Cobertura consolidado ficar abaixo do mínimo.

Uso: verificar-cobertura.py <Cobertura.xml> <minimo-em-%>
"""
import sys
import xml.etree.ElementTree as ET

relatorio, minimo = sys.argv[1], float(sys.argv[2])
raiz = ET.parse(relatorio).getroot()
linhas = float(raiz.get("line-rate", 0)) * 100
ramos = float(raiz.get("branch-rate", 0)) * 100

print(f"Cobertura de linhas: {linhas:.1f}% (mínimo {minimo:.0f}%) | ramos: {ramos:.1f}%")
if linhas < minimo:
    print(f"::error::Cobertura de linhas {linhas:.1f}% abaixo do mínimo de {minimo:.0f}%.")
    sys.exit(1)
