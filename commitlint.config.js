module.exports = {
	extends: ['@commitlint/config-conventional'],
	ignores: [commit => /^Merge /.test(commit)],
};
