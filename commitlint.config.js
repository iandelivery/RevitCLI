module.exports = {
	extends: ['@commitlint/config-conventional'],
	ignores: [
		commit => /^Merge /.test(commit),
		commit => /^chore: bump \S+ from \S+ to \S+/im.test(commit),
	],
};
