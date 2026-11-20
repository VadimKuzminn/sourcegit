#!/usr/bin/env bash
# Packs the app published to build/SourceGit into build/sourcegit_$VERSION.$RUNTIME.AppImage
# for the releases of the valuce fork (see .github/workflows/fork-release.yml). Unlike
# package.linux.sh it builds no deb/rpm and embeds no update information: the app updates
# itself from the fork's releases (see src/Models/SelfUpdater.cs).
#
# Needs curl and desktop-file-utils. Without FUSE (e.g. in a container) set APPIMAGE_EXTRACT_AND_RUN=1.

set -euo pipefail

case "$RUNTIME" in
  linux-x64) appimage_arch=x86_64 ;;
  *) echo "Unsupported RUNTIME: $RUNTIME" >&2; exit 1 ;;
esac

cd build

if [[ ! -x appimagetool ]]; then
  curl -fsSL -o appimagetool https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x appimagetool
fi

rm -f SourceGit/*.dbg SourceGit/*.pdb
mv SourceGit/SourceGit SourceGit/sourcegit
chmod -R 755 SourceGit

app=SourceGit.AppDir
rm -rf "$app"
mkdir -p "$app/opt" "$app/usr/share/metainfo" "$app/usr/share/applications"

cp -r SourceGit "$app/opt/sourcegit"
desktop-file-install resources/_common/applications/sourcegit.desktop --dir "$app/usr/share/applications" \
  --set-icon com.sourcegit_scm.SourceGit --set-key=Exec --set-value=AppRun
mv "$app/usr/share/applications/sourcegit.desktop" "$app/usr/share/applications/com.sourcegit_scm.SourceGit.desktop"
cp resources/_common/icons/sourcegit.png "$app/com.sourcegit_scm.SourceGit.png"
ln -rsf "$app/opt/sourcegit/sourcegit" "$app/AppRun"
ln -rsf "$app/usr/share/applications/com.sourcegit_scm.SourceGit.desktop" "$app"
cp resources/appimage/sourcegit.appdata.xml "$app/usr/share/metainfo/com.sourcegit_scm.SourceGit.appdata.xml"

ARCH="$appimage_arch" ./appimagetool "$app" "sourcegit_$VERSION.$RUNTIME.AppImage"
