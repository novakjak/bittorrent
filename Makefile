.PHONY: package package-win package-linux clean

package: package-win package-linux
package-win:
	dotnet publish -c Release -r win-x64 --self-contained true
	zip -q -r bittorrent-win64.zip bittorrent/bin/Release/net9.0/win-x64/publish

package-linux:
	dotnet publish -c Release -r linux-x64 --self-contained true
	tar czvf bittorrent-linux.tar.gz bittorrent/bin/Release/net9.0/linux-x64/publish

clean:
	dotnet clean
	rm *.zip *.tar.gz *.mkv *.log bittorrent/*.log bittorrent/*.mkv
