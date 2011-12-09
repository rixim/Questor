del .\questor\bin\debug\*.* /Q
del .\questor.modules\bin\debug\*.* /Q
mkdir output
del .\output\*.* /Q
copy .\questor\bin\debug\*.exe .\output\*.exe
copy .\questor\bin\debug\*.dll .\output\*.dll
copy .\gotobm\bin\debug\*.exe .\output\*.exe
copy .\updateinvtypes\bin\debug\*.exe .\output\*.exe
copy .\valuedump\bin\debug\*.exe .\output\*.exe
copy .\buylpi\bin\debug\*.exe .\output\*.exe
pause
