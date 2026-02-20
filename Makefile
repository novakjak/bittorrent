.PHONY: package package-win package-linux clean

package: package-win package-linux
package-win:
	dotnet publish -c Release -r win-x64 --self-contained true

	mkdir -p out
	cd out; mv ../BitAvalanche/bin/Release/net9.0/win-x64/publish BitAvalanche
	cd out; zip -q -r BitAvalanche-win64.zip BitAvalanche
	cd out; mv BitAvalanche-win64.zip ..
	rm -r out

package-linux:
	dotnet publish -c Release -r linux-x64 --self-contained true
	tar czvf BitAvalanche-linux.tar.gz BitAvalanche/bin/Release/net9.0/linux-x64/publish

clean:
	dotnet clean
	rm -r *.zip *.tar.gz *.mkv *.log BitAvalanche/*.log BitAvalanche/*.mkv out/
